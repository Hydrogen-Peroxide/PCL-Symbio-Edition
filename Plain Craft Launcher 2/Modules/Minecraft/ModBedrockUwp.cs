using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace PCL;

/// <summary>
///     基岩版 UWP 安装支持。
///     移植自 BedrockBoot / BedrockLauncher.Core：
///     https://github.com/Round-Studio/BedrockBoot
///     https://github.com/Round-Studio/BedrockLauncher.Core
/// </summary>
public static class ModBedrockUwp
{
    /// <summary>
    ///     获取版本对应的独立目录，供导入、下载、安装和删除流程共用。
    /// </summary>
    public static string GetVersionDirectory(string versionsDirectory, string versionId)
    {
        // 版本号可能来自安装包清单或远端列表，必须是单个目录名。
        // extracted 是解压缓存根目录，不能被当作普通版本覆盖或删除。
        if (string.IsNullOrWhiteSpace(versionId) || versionId is "." or ".." ||
            versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            versionId.IndexOfAny(['/', '\\', ':']) >= 0 ||
            versionId.EndsWith('.') || versionId.EndsWith(' ') ||
            versionId.Equals("extracted", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("基岩版版本标识必须是有效的独立目录名。", nameof(versionId));

        return Path.Combine(versionsDirectory, versionId);
    }

    /// <summary>
    ///     将安装路径作为 PowerShell 字符串字面量传入，避免路径中的引号或变量被解释为脚本。
    /// </summary>
    public static string GetAppxInstallArguments(string path, bool register = false)
    {
        var literalPath = "'" + path.Replace("'", "''") + "'";
        var command = $"Add-AppxPackage -{(register ? "Register" : "Path")} {literalPath} -ErrorAction Stop 2>&1";
        return "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }

    #region 版本号

    /// <summary>
    ///     对应 BedrockLauncher.Core 的 VersionsHelper.GetNextVersion。
    ///     生成一个更大的版本号，使旧版本包可以原地注册覆盖已安装的新版本（降级）。
    /// </summary>
    public static string GetNextVersion(Version currentVersion)
    {
        var ticks = DateTime.Now.Ticks;
        var seed = (int)(ticks & 0xFFFFFFFF) ^ (int)(ticks >> 32) ^ Environment.TickCount;
        var rand = new Random(seed);

        var sevenDigitStr = rand.Next(1_000_000, 10_000_000).ToString(CultureInfo.InvariantCulture);
        var positions = Enumerable.Range(0, 7).OrderBy(_ => rand.Next()).Take(2).ToArray();
        Array.Sort(positions);
        var buildSuffix = int.Parse(
            sevenDigitStr[positions[0]].ToString() + sevenDigitStr[positions[1]].ToString(),
            CultureInfo.InvariantCulture);

        var paddedBuild = currentVersion.Build.ToString(CultureInfo.InvariantCulture).PadRight(5, '0');
        var buildPrefix = paddedBuild.Substring(0, 3);
        var newBuild = int.Parse(buildPrefix + buildSuffix.ToString().PadLeft(2, '0'),
            CultureInfo.InvariantCulture);
        if (newBuild > 65535) newBuild %= 65535;
        if (newBuild == 0) newBuild = 1;

        var remainingIndices = Enumerable.Range(0, 7).Except(positions).OrderBy(i => i).ToArray();
        var revisionStr = new string(remainingIndices.Select(i => sevenDigitStr[i]).ToArray());
        var revision = int.Parse(revisionStr, CultureInfo.InvariantCulture);
        if (revision > 65535) revision %= 65535;
        if (revision == 0) revision = 1;

        return $"{currentVersion.Major}.{currentVersion.Minor}.{newBuild}.{revision}";
    }

    #endregion

    #region FE3 下载地址解析

    private const string SoapContentType = "application/soap+xml; charset=utf-8";

    private const string Fe3UpdateUri =
        "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured";

    private const string DeviceAttributes =
        "E:BranchReadinessLevel=CBB&DchuNvidiaGrfxExists=1&ProcessorIdentifier=Intel64%20Family%206%20Model%2063%20Stepping%202" +
        "&CurrentBranch=rs4_release&DataVer_RS5=1942&FlightRing=Retail&AttrDataVer=57&InstallLanguage=en-US" +
        "&DchuAmdGrfxExists=1&OSUILocale=en-US&InstallationType=Client&FlightingBranchName=&Version_RS5=10" +
        "&UpgEx_RS5=Green&GStatus_RS5=2&OSSkuId=48&App=WU&InstallDate=1529700913&ProcessorManufacturer=GenuineIntel" +
        "&AppVer=10.0.17134.471&OSArchitecture=AMD64&UpdateManagementGroup=2&IsDeviceRetailDemo=0" +
        "&HidOverGattReg=C%3A%5CWINDOWS%5CSystem32%5CDriverStore%5CFileRepository%5Chidbthle.inf_amd64_467f181075371c89" +
        "%5CMicrosoft.Bluetooth.Profiles.HidOverGatt.dll&IsFlightingEnabled=0&DchuIntelGrfxExists=1&TelemetryLevel=1" +
        "&DefaultUserRegion=244&DeferFeatureUpdatePeriodInDays=365&Bios=Unknown&WuClientVer=10.0.17134.471" +
        "&PausedFeatureStatus=1&Steam=URL%3Asteam%20protocol&Free=8to16&OSVersion=10.0.17134.472&DeviceFamily=Windows.Desktop";

    private const string Fe3FileUrlTemplate = """
                                            <s:Envelope xmlns:a="http://www.w3.org/2005/08/addressing" xmlns:s="http://www.w3.org/2003/05/soap-envelope">
                                              <s:Header>
                                                <a:Action s:mustUnderstand="1">http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/GetExtendedUpdateInfo2</a:Action>
                                                <a:MessageID>urn:uuid:5754a03d-d8d5-489f-b24d-efc31b3fd32d</a:MessageID>
                                                <a:To s:mustUnderstand="1">https://fe3.delivery.mp.microsoft.com/ClientWebService/Client.asmx/secured</a:To>
                                                <o:Security s:mustUnderstand="1" xmlns:o="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">
                                                  <Timestamp xmlns="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">
                                                    <Created></Created>
                                                    <Expires></Expires>
                                                  </Timestamp>
                                                  <wuws:WindowsUpdateTicketsToken wsu:id="ClientMSA" xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd" xmlns:wuws="http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization">
                                                    <TicketType Name="AAD" Version="1.0" Policy="MBI_SSL"></TicketType>
                                                  </wuws:WindowsUpdateTicketsToken>
                                                </o:Security>
                                              </s:Header>
                                              <s:Body>
                                                <GetExtendedUpdateInfo2 xmlns="http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService">
                                                  <updateIDs>
                                                    <UpdateIdentity>
                                                      <UpdateID></UpdateID>
                                                      <RevisionNumber>1</RevisionNumber>
                                                    </UpdateIdentity>
                                                  </updateIDs>
                                                  <infoTypes>
                                                    <XmlUpdateFragmentType>FileUrl</XmlUpdateFragmentType>
                                                  </infoTypes>
                                                  <deviceAttributes></deviceAttributes>
                                                </GetExtendedUpdateInfo2>
                                              </s:Body>
                                            </s:Envelope>
                                            """;

    /// <summary>
    ///     用 Microsoft Store FE3 接口把旧版 UWP 的 UpdateID（GUID）解析为真实下载地址。
    ///     对应 BedrockLauncher.Core 的 UpdateIDHelper.GetUriAsync。
    /// </summary>
    public static async Task<string?> ResolveDownloadUrlAsync(string updateId)
    {
        try
        {
            var now = DateTime.UtcNow;
            var doc = new XmlDocument();
            doc.LoadXml(Fe3FileUrlTemplate);
            doc.GetElementsByTagName("UpdateID")[0]!.InnerText = updateId;
            doc.GetElementsByTagName("Created")[0]!.InnerText = now.ToString("o");
            doc.GetElementsByTagName("Expires")[0]!.InnerText = now.AddMinutes(5).ToString("o");
            doc.GetElementsByTagName("deviceAttributes")[0]!.InnerText = DeviceAttributes;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", SoapContentType);
            using var content = new StringContent(doc.InnerXml, Encoding.UTF8, "application/soap+xml");
            using var response = await client.PostAsync(Fe3UpdateUri, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var soapResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var xdoc = XDocument.Parse(soapResponse);
            XNamespace ns = "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService";
            var urls = xdoc.Descendants(ns + "Url")
                .Select(x => WebUtility.HtmlDecode(x.Value).Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            if (urls.Count == 0) return null;

            // 优先取带签名参数的完整下载地址（同 BedrockLauncher.Core 的判定方式）
            var complexUrl = urls.FirstOrDefault(url =>
                url.Contains("?P1=") || url.Contains("tlu.dl.") || url.Contains("&P2=") ||
                url.Contains("%3d") || url.Length > 150);
            return complexUrl ?? urls[^1];
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] FE3 解析下载地址失败 ({updateId}): {ex.Message}", ModBase.LogLevel.Normal);
            return null;
        }
    }

    #endregion

    #region Manifest 改写

    /// <summary>
    ///     CustomCapability.SCCD 内容（微软 coreAppActivation 受限能力声明）。
    /// </summary>
    private const string SccdBase64 =
        "PD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTgiPz4KPEN1c3RvbUNhcGFiaWxpdHlEZXNjcmlwdG9yIHhtbG5zPSJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FwcHgvMjAxOC9zY2NkIiB4bWxuczpzPSJodHRwOi8vc2NoZW1hcy5taWNyb3NvZnQuY29tL2FwcHgvMjAxOC9zY2NkIj4KICA8Q3VzdG9tQ2FwYWJpbGl0aWVzPgogICAgPEN1c3RvbUNhcGFiaWxpdHkgTmFtZT0iTWljcm9zb2Z0LmNvcmVBcHBBY3RpdmF0aW9uXzh3ZWt5YjNkOGJid2UiPjwvQ3VzdG9tQ2FwYWJpbGl0eT4KICA8L0N1c3RvbUNhcGFiaWxpdGllcz4KICA8QXV0aG9yaXplZEVudGl0aWVzIEFsbG93QW55PSJ0cnVlIi8+CiAgPENhdGFsb2c+RkZGRjwvQ2F0YWxvZz4KPC9DdXN0b21DYXBhYmlsaXR5RGVzY3JpcHRvcj4=";

    /// <summary>
    ///     重写后的 Extensions 节点，用于替换旧版本 manifest 中的自闭合 &lt;Extensions /&gt;。
    /// </summary>
    private const string ExtensionsXml = """
                                          <Extensions>
                                                 <uap4:Extension Category="windows.loopbackAccessRules">
                                                   <uap4:LoopbackAccessRules>
                                                     <uap4:Rule Direction="out" PackageFamilyName="Microsoft.MEECC_8wekyb3d8bbwe" />
                                                   </uap4:LoopbackAccessRules>
                                                 </uap4:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcperf">
                                                     <uap:DisplayName>MCPERF</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import world</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCPERF</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcshortcut">
                                                     <uap:DisplayName>MCSHORTCUT</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and load world</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCSHORTCUT</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcpack">
                                                     <uap:DisplayName>MCPACK</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import resource pack</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCPACK</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcworld">
                                                     <uap:DisplayName>MCWORLD</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import world</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCWORLD</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcproject">
                                                     <uap:DisplayName>MCPROJECT</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import project</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCPROJECT</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mceditoraddon">
                                                     <uap:DisplayName>MCEDITORADDON</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import editor addon</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCEDITORADDON</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.protocol">
                                                   <uap:Protocol Name="ms-xbl-multiplayer" />
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.protocol">
                                                   <uap:Protocol Name="minecraft" />
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mcaddon">
                                                     <uap:DisplayName>MCADDON</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import addon</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCADDON</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                                 <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="App2">
                                                   <uap:FileTypeAssociation Name="mctemplate">
                                                     <uap:DisplayName>MCTEMPLATE</uap:DisplayName>
                                                     <uap:InfoTip>Launch Minecraft and import world template</uap:InfoTip>
                                                     <uap:SupportedFileTypes>
                                                       <uap:FileType>.MCTEMPLATE</uap:FileType>
                                                     </uap:SupportedFileTypes>
                                                   </uap:FileTypeAssociation>
                                                 </uap:Extension>
                                               </Extensions>
                                          """;

    /// <summary>
    ///     对应 BedrockLauncher.Core 的 ManifestEditor.EditManifest。
    ///     旧版本包直接注册会因版本号更低、缺少受信任能力而失败，这里按 BedrockBoot 的方式改写 manifest。
    /// </summary>
    public static void EditManifest(string directory, string gameName)
    {
        var manifestPath = Path.Combine(directory, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("找不到 AppxManifest.xml", manifestPath);

        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
        XNamespace uap4 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/4";
        XNamespace uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
        XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        XNamespace desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";

        var doc = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
        var package = doc.Root ?? throw new Exception("AppxManifest.xml 内容为空");

        UpdateIgnorableNamespaces(package, rescap, uap4, uap10);
        UpdateApplicationTrustLevel(package, ns, uap10);
        UpdateCapabilities(package, ns, rescap, uap4);

        var applications = package.Element(ns + "Applications");
        var application = applications?.Element(ns + "Application");
        if (application is null)
            throw new Exception("AppxManifest.xml 中找不到 Applications/Application 节点");

        var extensionElement = application.Element(ns + "Extensions");
        if (extensionElement is null)
        {
            extensionElement = new XElement(ns + "Extensions");
            application.Add(extensionElement);
        }
        else
        {
            extensionElement.RemoveAll();
        }

        var identity = package.Element(ns + "Identity");
        var versionText = identity?.Attribute("Version")?.Value;
        if (identity is not null && Version.TryParse(versionText, out var currentVersion))
            identity.SetAttributeValue("Version", GetNextVersion(currentVersion));

        application.SetAttributeValue(desktop4 + "SupportsMultipleInstances", "true");

        var visualElements = application.Element(uap + "VisualElements");
        if (visualElements is not null)
        {
            if (!string.IsNullOrEmpty(gameName))
                visualElements.SetAttributeValue("DisplayName", gameName);
            visualElements.SetAttributeValue("AppListEntry", "none");
        }

        doc.Save(manifestPath, SaveOptions.DisableFormatting);

        File.WriteAllBytes(Path.Combine(directory, "CustomCapability.SCCD"), Convert.FromBase64String(SccdBase64));

        // XDocument 会把空节点写成 <Extensions />，这里用完整节点替换
        var text = File.ReadAllText(manifestPath);
        var selfClosing = Regex.Match(text, @"<\s*Extensions\s*/\s*>");
        if (selfClosing.Success)
            File.WriteAllText(manifestPath, text.Replace(selfClosing.Value, ExtensionsXml));
    }

    private static void UpdateIgnorableNamespaces(XElement package, XNamespace rescap, XNamespace uap4,
        XNamespace uap10)
    {
        XNamespace desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
        string[] requiredNamespaces = ["uap", "uap4", "uap10", "rescap", "desktop4"];

        var ignorable = package.Attribute("IgnorableNamespaces");
        if (ignorable is null)
        {
            package.SetAttributeValue("IgnorableNamespaces", string.Join(" ", requiredNamespaces));
        }
        else
        {
            var existing = ignorable.Value.Split([' '], StringSplitOptions.RemoveEmptyEntries);
            var missing = requiredNamespaces.Except(existing);
            if (missing.Any())
                ignorable.Value = string.Join(" ", existing.Concat(missing));
        }

        package.SetAttributeValue(XNamespace.Xmlns + "desktop4", desktop4.NamespaceName);
        package.SetAttributeValue(XNamespace.Xmlns + "rescap", rescap.NamespaceName);
        package.SetAttributeValue(XNamespace.Xmlns + "uap4", uap4.NamespaceName);
        package.SetAttributeValue(XNamespace.Xmlns + "uap10", uap10.NamespaceName);
    }

    private static void UpdateApplicationTrustLevel(XElement package, XNamespace ns, XNamespace uap10)
    {
        var application = package.Element(ns + "Applications")?.Element(ns + "Application");
        application?.SetAttributeValue(uap10 + "TrustLevel", "mediumIL");
    }

    private static void UpdateCapabilities(XElement package, XNamespace ns, XNamespace rescap, XNamespace uap4)
    {
        var capabilities = package.Element(ns + "Capabilities");
        if (capabilities is null) return;

        capabilities.Elements(rescap + "Capability").Remove();
        capabilities.Elements(uap4 + "CustomCapability").Remove();

        var deviceCapabilities = capabilities.Elements(ns + "DeviceCapability").ToList();
        deviceCapabilities.ForEach(c => c.Remove());

        capabilities.Add(
            new XElement(rescap + "Capability", new XAttribute("Name", "runFullTrust")),
            new XElement(uap4 + "CustomCapability", new XAttribute("Name", "Microsoft.coreAppActivation_8wekyb3d8bbwe"))
        );

        if (deviceCapabilities.Count > 0)
            deviceCapabilities.ForEach(capabilities.Add);
        else
            capabilities.Add(new XElement(ns + "DeviceCapability", new XAttribute("Name", "internetClient")));
    }

    #endregion

    #region 包与进程

    /// <summary>
    ///     已安装的基岩版包名（UWP 正式版）。
    /// </summary>
    public const string ReleasePackageName = "Microsoft.MinecraftUWP";

    /// <summary>
    ///     旧版本的进程名（1.16 及更早），新版为 Minecraft.Windows。
    /// </summary>
    private static readonly string[] BedrockProcessNames = ["Minecraft.Windows", "Minecraft.Win10.DX11"];

    /// <summary>
    ///     结束基岩版进程。
    /// </summary>
    public static void StopMinecraftProcesses()
    {
        foreach (var name in BedrockProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        ModBase.Log($"[Bedrock] 结束进程 {name} 失败: {ex.Message}", ModBase.LogLevel.Normal);
                    }
                }
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 枚举进程 {name} 失败: {ex.Message}", ModBase.LogLevel.Normal);
            }
        }
    }

    /// <summary>
    ///     获取已安装的基岩版包信息（包名与版本号），未安装时返回 null。
    /// </summary>
    public static (string FullName, string Version, string FamilyName)? GetInstalledPackage(string packageName = ReleasePackageName)
    {
        try
        {
            var output = ModBase.ShellAndGetOutputChecked("powershell.exe",
                $"-NoProfile -Command \"Get-AppxPackage -Name '{packageName}' -ErrorAction Stop | Select-Object -First 1 -Property PackageFullName,Version,PackageFamilyName | ConvertTo-Csv -NoTypeInformation\"",
                timeout: 20000);
            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return null;

            var parts = lines[1].Split(',');
            if (parts.Length < 3) return null;

            return (parts[0].Trim().Trim('"'), parts[1].Trim().Trim('"'), parts[2].Trim().Trim('"'));
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 获取已安装包信息失败: {ex.Message}", ModBase.LogLevel.Normal);
            return null;
        }
    }

    /// <summary>
    ///     卸载已安装的基岩版，并保留存档等应用数据。
    /// </summary>
    public static void UninstallPreservingData()
    {
        StopMinecraftProcesses();

        // 只处理旧版 UWP 包（Microsoft.MinecraftUWP），避免误卸新版 GDK 包（Microsoft.Minecraft）
        // PreserveApplicationData 需要 Windows 10 1809 及以上
        var result = ModBase.ShellAndGetOutputChecked("powershell.exe",
            $"-NoProfile -Command \"$p = Get-AppxPackage -Name '{ReleasePackageName}' -ErrorAction Stop; if ($p) {{ foreach ($x in $p) {{ Remove-AppxPackage -Package $x.PackageFullName -PreserveApplicationData -ErrorAction Stop }} }} else {{ Write-Output 'None' }}\"",
            timeout: 120000);

        ModBase.Log($"[Bedrock] 卸载结果: {result.Trim()}", ModBase.LogLevel.Normal);

        var verify = ModBase.ShellAndGetOutputChecked("powershell.exe",
            $"-NoProfile -Command \"if (Get-AppxPackage -Name '{ReleasePackageName}' -ErrorAction Stop) {{ 'StillInstalled' }} else {{ 'Cleared' }}\"",
            timeout: 20000);

        // 普通卸载会删除应用数据。无法确认保留数据的卸载成功时必须中止，不能降级为普通卸载。
        if (verify.Trim() != "Cleared")
            throw new InvalidOperationException(
                $"无法确认基岩版已保留数据卸载，已中止安装以保护存档。{Environment.NewLine}{result.Trim()}{Environment.NewLine}{verify.Trim()}");
    }

    /// <summary>
    ///     通过 shell:AppsFolder 启动已安装的 UWP 应用（等价于 BedrockBoot 的 AppDiagnosticInfo.LaunchAsync）。
    ///     Manifest 被设为 AppListEntry=none 后无法从开始菜单启动，但 AppsFolder 仍可用。
    /// </summary>
    public static bool LaunchPackage(string? targetPackageName = null)
    {
        // 未指定包名时，先尝试旧版 UWP 包（Microsoft.MinecraftUWP），再尝试新版 GDK 包（Microsoft.Minecraft）
        var packageNames = string.IsNullOrEmpty(targetPackageName)
            ? new[] { ReleasePackageName, "Microsoft.Minecraft" }
            : new[] { targetPackageName };
        foreach (var packageName in packageNames)
        {
            var info = GetInstalledPackage(packageName);
            if (info is null) continue;

            // Application Id 由 Manifest 决定（UWP 版为 Game，而非 App），拼错会让 explorer 打开文档文件夹
            var appId = GetApplicationId(packageName) ?? (packageName == ReleasePackageName ? "Game" : "App");
            var aumid = $"{info.Value.FamilyName}!{appId}";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 通过 AppsFolder 启动 {aumid} 失败: {ex.Message}", ModBase.LogLevel.Normal);
            }
        }

        return false;
    }

    /// <summary>
    ///     以编辑器模式启动基岩版。
    ///     Editor 模式无法通过 shell:AppsFolder 进入，只能由 creator 协议唤起；
    ///     正式版与预览版各自注册了协议，先试正式版再回退预览版。
    /// </summary>
    public static bool LaunchEditor()
    {
        foreach (var uri in new[] { "minecraft://creator/?Editor=true", "minecraft-preview://creator/?Editor=true" })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
                ModBase.Log($"[Bedrock] 正在通过 {uri} 以编辑器模式启动 Minecraft Bedrock Edition...");
                return true;
            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 通过 {uri} 启动编辑器失败: {ex.Message}", ModBase.LogLevel.Normal);
            }
        }

        return false;
    }

    /// <summary>
    ///     从已安装包的 AppxManifest 中读取 Application Id。
    /// </summary>
    private static string? GetApplicationId(string packageName = ReleasePackageName)
    {
        try
        {
            var output = ModBase.ShellAndGetOutputChecked("powershell.exe",
                "-NoProfile -Command \"$p = Get-AppxPackage -Name '" + packageName + "' -ErrorAction Stop | Select-Object -First 1; " +
                "if ($p) { $ids = @((Get-AppxPackageManifest $p -ErrorAction Stop).Package.Applications.Application.Id); Write-Output $ids[0] }\"",
                timeout: 20000);
            var id = output.Trim();
            if (!string.IsNullOrEmpty(id) && !id.Contains(' '))
                return id;
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 获取 Application Id 失败: {ex.Message}", ModBase.LogLevel.Normal);
        }
        return null;
    }

    #endregion

    #region UWP 依赖

    private static readonly (string Name, string? MinVersion)[] UwpDependencies =
    [
        ("Microsoft.VCLibs.140.00", "14.0.33519.0"),
        ("Microsoft.NET.Native.Runtime.1.4", null),
        ("Microsoft.NET.Native.Runtime.2.2", "2.2.28604.0"),
        ("Microsoft.VCLibs.140.00.UWPDesktop", null),
        ("Microsoft.Services.Store.Engagement", null),
        ("Microsoft.NET.Native.Framework.1.3", null),
        ("Microsoft.NET.Native.Framework.2.2", "2.2.29512.0"),
        ("Microsoft.GamingServices", "33.108.12001.0")
    ];

    /// <summary>
    ///     对应 BedrockBoot 的 UwpDependencyChecker.GetMissingDependencies。
    /// </summary>
    public static List<(string Name, string? MinVersion)> GetMissingDependencies()
    {
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var output = ModBase.ShellAndGetOutputChecked("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -ErrorAction Stop | Select-Object Name,Version | ConvertTo-Csv -NoTypeInformation\"",
                timeout: 60000);

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var start = lines.Length > 0 && lines[0].Contains("Name") && lines[0].Contains("Version") ? 1 : 0;
            for (var i = start; i < lines.Length; i++)
            {
                var parts = ParseCsvLine(lines[i].Trim());
                if (parts.Count < 2) continue;
                var name = parts[0].Trim().Trim('"');
                var version = parts[1].Trim().Trim('"');
                if (string.IsNullOrEmpty(name)) continue;

                if (installed.TryGetValue(name, out var old))
                {
                    if (CompareVersions(version, old) > 0) installed[name] = version;
                }
                else
                {
                    installed[name] = version;
                }
            }
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 检测 UWP 依赖失败: {ex.Message}", ModBase.LogLevel.Normal);
            throw;
        }

        var missing = new List<(string Name, string? MinVersion)>();
        foreach (var dependency in UwpDependencies)
        {
            if (!installed.TryGetValue(dependency.Name, out var installedVersion))
            {
                missing.Add(dependency);
                continue;
            }

            if (!string.IsNullOrEmpty(dependency.MinVersion) &&
                CompareVersions(installedVersion, dependency.MinVersion) < 0)
                missing.Add(dependency);
        }

        return missing;
    }

    /// <summary>
    ///     下载并安装缺失的 UWP 依赖（对应 BedrockBoot 的依赖安装流程）。
    ///     失败不会中断安装，仅记录日志。
    /// </summary>
    public static async Task EnsureDependenciesAsync()
    {
        List<(string Name, string? MinVersion)> missing;
        try
        {
            missing = GetMissingDependencies();
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 依赖检查异常，跳过: {ex.Message}", ModBase.LogLevel.Normal);
            return;
        }

        if (missing.Count == 0)
        {
            ModBase.Log("[Bedrock] UWP 依赖检查通过", ModBase.LogLevel.Normal);
            return;
        }

        ModBase.Log($"[Bedrock] 缺失 {missing.Count} 个 UWP 依赖: {string.Join(", ", missing.Select(m => m.Name))}",
            ModBase.LogLevel.Normal);

        foreach (var (name, minVersion) in missing)
        {
            string? tempPath = null;
            try
            {
                var url = await GetUwpPackageDownloadUrlAsync(name, minVersion).ConfigureAwait(false);
                if (string.IsNullOrEmpty(url))
                {
                    ModBase.Log($"[Bedrock] 无法获取依赖 {name} 的下载地址，跳过", ModBase.LogLevel.Normal);
                    continue;
                }

                tempPath = Path.Combine(Path.GetTempPath(), $"{name}_{Guid.NewGuid():N}.appx");
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    var bytes = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                }

                _ = ModBase.ShellAndGetOutputChecked("powershell.exe",
                    GetAppxInstallArguments(tempPath),
                    timeout: 300000);

                ModBase.Log($"[Bedrock] 依赖 {name} 安装完成", ModBase.LogLevel.Normal);

            }
            catch (Exception ex)
            {
                ModBase.Log($"[Bedrock] 安装依赖 {name} 失败: {ex.Message}", ModBase.LogLevel.Normal);
            }
            finally
            {
                if (tempPath is not null) try { File.Delete(tempPath); } catch { }
            }
        }
    }

    /// <summary>
    ///     对应 BedrockBoot 的 UwpFileUrl.GetUwpPackageDownloadUrl。
    /// </summary>
    private static async Task<string?> GetUwpPackageDownloadUrlAsync(string packageName, string? minVersion)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Add("Origin", "https://store.rg-adguard.net");
            client.DefaultRequestHeaders.Add("Referer", "https://store.rg-adguard.net/");

            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("type", "PackageFamilyName"),
                new KeyValuePair<string, string>("url", $"{packageName}_8wekyb3d8bbwe"),
                new KeyValuePair<string, string>("ring", "RP"),
                new KeyValuePair<string, string>("lang", "en-US")
            ]);

            using var response = await client.PostAsync("https://store.rg-adguard.net/api/GetFiles", content)
                .ConfigureAwait(false);
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var regex = new Regex(@"<a\s+href=""([^""]+)""[^>]*>([^<]+\.(?:appx|appxbundle|msixbundle|msix))</a>",
                RegexOptions.IgnoreCase);
            string? bestUrl = null;
            string? bestVersion = null;

            foreach (Match match in regex.Matches(html))
            {
                var url = match.Groups[1].Value;
                var fileName = match.Groups[2].Value.ToLowerInvariant();
                if (!fileName.Contains("x64") && !fileName.Contains("neutral")) continue;

                var versionMatch = Regex.Match(fileName, @"(\d+\.\d+\.\d+\.\d+)");
                var version = versionMatch.Success ? versionMatch.Groups[1].Value : null;

                if (bestUrl is null || CompareVersions(version, bestVersion) > 0)
                {
                    bestUrl = url;
                    bestVersion = version;
                }
            }

            if (!string.IsNullOrEmpty(minVersion) && CompareVersions(bestVersion, minVersion) < 0)
                return null;

            return bestUrl;
        }
        catch (Exception ex)
        {
            ModBase.Log($"[Bedrock] 查询依赖 {packageName} 下载地址失败: {ex.Message}", ModBase.LogLevel.Normal);
            return null;
        }
    }

    private static int CompareVersions(string? version1, string? version2)
    {
        if (string.Equals(version1, version2, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrEmpty(version1)) return -1;
        if (string.IsNullOrEmpty(version2)) return 1;

        if (Version.TryParse(version1, out var v1) && Version.TryParse(version2, out var v2))
            return v1.CompareTo(v2);

        return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    #endregion
}
