using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;

namespace PCL;

public static class ModFolder
{
    /// <summary>
    ///     当前的 Minecraft 文件夹路径，以"\"结尾。
    /// </summary>
    public static string mcFolderSelected;

    /// <summary>
    ///     当前的 Minecraft 文件夹列表。
    /// </summary>
    public static List<McFolder> mcFolderList = new();

    public class McFolder // 必须是 Class，否则不是引用类型，在 ForEach 中不会得到刷新
    {
        public enum Types
        {
            Original,
            RenamedOriginal,
            Custom
        }

        /// <summary>
        ///     文件夹路径。
        ///     以 \ 结尾，例如 "D:\Game\MC\.minecraft\"。
        /// </summary>
        public string Location;

        public string Name;
        public Types type;

        public override bool Equals(object obj)
        {
            if (obj is not McFolder)
                return false;
            var folder = (McFolder)obj;
            return (Name ?? "") == (folder.Name ?? "") && (Location ?? "") == (folder.Location ?? "") &&
                   type == folder.type;
        }

        public override string ToString()
        {
            return Location;
        }
    }

    /// <summary>
    ///     加载 Minecraft 文件夹列表。
    /// </summary>
    public static ModLoader.LoaderTask<int, int> mcFolderListLoader = new("Minecraft Folder List",
        _ => McFolderListLoadSub(), priority: ThreadPriority.AboveNormal);

    private static void McFolderListLoadSub()
    {
        try
        {
            // 初始化
            var cacheMcFolderList = new List<McFolder>();

            #region 读取自定义（Custom）文件夹，可能没有结果

            // 格式：TMZ 12>C://xxx/xx/|Test>D://xxx/xx/|名称>路径
            foreach (string folder in (IEnumerable)((dynamic)States.Game.Folders).Split("|"))
            {
                if (string.IsNullOrEmpty(folder))
                    continue;
                if (!folder.Contains(">") || !folder.EndsWithF(@"\"))
                {
                    HintService.Hint(Lang.Text("Select.Folder.Invalid", folder), HintType.Error);
                    continue;
                }

                var name = folder.Split(">")[0];
                var path = folder.Split(">")[1];
                try
                {
                    ModBase.CheckPermissionWithException(path);
                    cacheMcFolderList.Add(new McFolder { Name = name, Location = path, type = McFolder.Types.Custom });
                }
                catch (Exception ex)
                {
                    ModMain.MyMsgBox(
                        Lang.Text("Select.Folder.Invalid.WithDetail", path, ex.ToString()),
                        Lang.Text("Select.Folder.InvalidTitle"), isWarn: true);
                    ModBase.Log(ex, $"无法访问 Minecraft 文件夹 {path}");
                }
            }

            #endregion

            #region 读取默认（Original）文件夹，即当前、官启文件夹，可能没有结果

            var currentMcFolderList = new List<McFolder>();
            var originalMcFolderList = new List<McFolder>();
            // 扫描当前文件夹
            try
            {
                if (Directory.Exists(ModBase.exePath + @"versions\"))
                    originalMcFolderList.Add(new McFolder
                        { Name = Lang.Text("Select.Folder.CurrentFolder"), Location = ModBase.exePath, type = McFolder.Types.Original });
                foreach (var folder in new DirectoryInfo(ModBase.exePath).GetDirectories())
                    if (Directory.Exists(Path.Combine(folder.FullName, "versions")) || folder.Name == ".minecraft")
                    {
                        var newCurrentFolder = new McFolder
                            { Name = folder.Name, Location = folder.FullName + @"\", type = McFolder.Types.Original };
                        originalMcFolderList.Add(newCurrentFolder);
                        currentMcFolderList.Add(newCurrentFolder);
                    }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "扫描 PCL 所在文件夹中是否有 MC 文件夹失败");
            }

            // 扫描官启文件夹
            var mojangPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") + @"\";
            if ((!currentMcFolderList.Any() || (mojangPath ?? "") != (currentMcFolderList[0].Location ?? "")) &&
                Directory.Exists(Path.Combine(mojangPath, "versions"))) // 当前文件夹不是官启文件夹
                // 具有权限且存在 versions 文件夹
                originalMcFolderList.Add(new McFolder
                    { Name = Lang.Text("Select.Folder.OfficialLauncherFolder"), Location = mojangPath, type = McFolder.Types.Original });

            ModBase.Log(cacheMcFolderList.Count + " 个自定义文件夹，" + originalMcFolderList.Count + " 个原始文件夹");

            foreach (var newOriginalFolder in originalMcFolderList)
            {
                var unAdded = true;
                foreach (var cacheFolder in cacheMcFolderList)
                    if ((cacheFolder.Location ?? "") == (newOriginalFolder.Location ?? ""))
                    {
                        if ((cacheFolder.Name ?? "") != (newOriginalFolder.Name ?? ""))
                            cacheFolder.type = McFolder.Types.RenamedOriginal;
                        else
                            cacheFolder.type = McFolder.Types.Original;
                        unAdded = false;
                    }

                if (unAdded)
                    cacheMcFolderList.Add(newOriginalFolder); // 如果没有重命名，则添加当前文件夹
            }

            #endregion

            #region 读取自定义文件夹情况并写入设置

            // 将自定义文件夹情况同步到设置
            var config = new List<string>();
            foreach (var Folder in cacheMcFolderList)
                config.Add(Folder.Name + ">" + Folder.Location);
            if (!config.Any())
                config.Add(""); // 防止 0 元素 Join 返回 Nothing
            States.Game.Folders = config.Join("|");

            #endregion

            // 若没有可用文件夹，则创建 .minecraft
            if (!cacheMcFolderList.Any())
            {
                Directory.CreateDirectory(ModBase.exePath + @".minecraft\versions\");
                cacheMcFolderList.Add(new McFolder
                    { Name = Lang.Text("Select.Folder.CurrentFolder"), Location = ModBase.exePath + @".minecraft\", type = McFolder.Types.Original });
            }

            foreach (var Folder in cacheMcFolderList) McFolderLauncherProfilesJsonCreate(Folder.Location);
            if (Config.Debug.AddRandomDelay)
                Thread.Sleep(RandomUtils.NextInt(200, 2000));

            // 回设
            mcFolderList = cacheMcFolderList;
        }

        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Select.Folder.Error.Load"),
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Select.Folder.Error.Load"));
        }
    }

    /// <summary>
    ///     将指定文件夹添加到 Minecraft 文件夹列表，并选中它。
    /// </summary>
    public static void AddFolder(string folderPath, string displayName, bool showHint)
    {
        // 检查文件夹权限
        // 检查实际的 Minecraft 文件夹位置（没有问题，或是在子文件夹中）
        // 判断是否已经添加过，若添加过则直接修改自定义名
        // 如果没有添加过，则添加进去
        // 保存
        // 切换选择并更新列表
        // 提示
        // 检查是否为根目录整合包，自动关闭版本隔离
        // 1. 根目录中存在数个 Mod
        // 2. 实例数较少，可能为整合包
        // 3. 能够找到可安装 Mod 的实例
        // 4. 该实例的隔离文件夹下不存在 mods
        // 满足以上全部条件则视为根目录整合包
        ModBase.RunInThread(() =>
        {
            try
            {
                if (!folderPath.EndsWith(@"\")) folderPath += @"\";
                if (!ModBase.CheckPermission(folderPath))
                {
                    if (!showHint) throw new Exception("PCL 没有访问文件夹的权限：" + folderPath);
                    HintService.Hint(Lang.Text("Select.Folder.AccessDenied"), HintType.Error);
                    return;
                }

                if (!ModBase.CheckPermission(folderPath + @"versions\"))
                    foreach (var Folder in new DirectoryInfo(folderPath).GetDirectories())
                        if (ModBase.CheckPermission(Path.Combine(Folder.FullName, "versions")))
                        {
                            folderPath = Folder.FullName + @"\";
                            break;
                        }

                var folders = new List<string>(States.Game.Folders.Split("|"));
                var isAdded = false;
                var isReplace = false;
                for (int i = 0, loopTo = folders.Count - 1; i <= loopTo; i++)
                {
                    var folder = folders[i];
                    if (string.IsNullOrEmpty(folder)) continue;
                    if (folder.Split(">")[1] != (folderPath ?? "")) continue;
                    isAdded = true;
                    if (folder.Split(">")[0] == displayName)
                    {
                        if (showHint) HintService.Hint(Lang.Text("Select.Folder.AlreadyInList"));
                        return;
                    }

                    folders[i] = $"{displayName}>{folderPath}";
                    isReplace = true;
                    if (showHint)
                        HintService.Hint(Lang.Text("Select.Folder.NameUpdated", displayName), HintType.Success);
                    break;
                }

                if (!isAdded) folders.Add($"{displayName}>{folderPath}");
                States.Game.Folders = folders.ToArray().Join("|");
                States.Game.SelectedFolder = folderPath.Replace(ModBase.exePath, "$");
                ModFolder.mcFolderListLoader.Start(isForceRestart: true);
                if (isReplace) return;
                if (showHint) HintService.Hint(Lang.Text("Select.Folder.Added", displayName), HintType.Success);
                var modFolder = new DirectoryInfo(folderPath + @"mods\");
                if (!(modFolder.Exists && modFolder.EnumerateFiles().Count() >= 3)) return;
                var versionFolder = new DirectoryInfo(folderPath + @"versions\");
                if (!(versionFolder.Exists && versionFolder.EnumerateDirectories().Count() <= 3)) return;
                foreach (var VersionPath in versionFolder.EnumerateDirectories())
                {
                    var version = new McInstance(VersionPath.FullName);
                    version.Load();
                    if (!version.Modable) continue;
                    var modIndieFolder = new DirectoryInfo(version.PathInstance + @"mods\");
                    if (modIndieFolder.Exists && modIndieFolder.EnumerateFiles().Any()) return;
                    Config.Instance.IndieV1[version.PathInstance] = 2;
                    Config.Instance.IndieV2[version.PathInstance] = false;
                    ModBase.Log("[Setup] 已自动关闭单版本隔离：" + version.Name, ModBase.LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Select.Folder.Error.AddNew"),
                    ModBase.LogLevel.Feedback,
                    userSummary: Lang.Text("Select.Folder.Error.AddNew"));
            }
        });
    }

    public static void RefreshInstances(string folder, bool clearCache = false)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (clearCache) ModBase.WriteIni(Path.Combine(folder, "PCL.ini"), "InstanceCache", "");
        if (string.Equals(folder, mcFolderSelected, StringComparison.OrdinalIgnoreCase))
            ModLoader.LoaderFolderRun(ModInstanceList.mcInstanceListLoader, folder,
                clearCache ? ModLoader.LoaderFolderRunType.ForceRun : ModLoader.LoaderFolderRunType.RunOnUpdated,
                1, @"versions\");
    }

    /// <summary>
    ///     为 Minecraft 文件夹创建 launcher_profiles.json 文件。
    /// </summary>
    public static void McFolderLauncherProfilesJsonCreate(string folder)
    {
        try
        {
            if (File.Exists(Path.Combine(folder, "launcher_profiles.json")))
                return;
            var now = DateTime.Now;
            var resultJson = @"{
    ""profiles"":  {
        ""PCL"": {
            ""icon"": ""Grass"",
            ""name"": ""PCL"",
            ""lastVersionId"": ""latest-release"",
            ""type"": ""latest-release"",
            ""lastUsed"": """ + now.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture) + "T" +
                             now.ToString("HH':'mm':'ss", CultureInfo.InvariantCulture) + @".0000Z""
        }
    },
    ""selectedProfile"": ""PCL"",
    ""clientToken"": ""23323323323323323323323323323333""
}";
            ModBase.WriteFile(Path.Combine(folder, "launcher_profiles.json"), resultJson, encoding: Encoding.GetEncoding("GB18030"));
            ModBase.Log("[Minecraft] 已创建 launcher_profiles.json：" + folder);
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                "创建 launcher_profiles.json 失败（" + folder + "）",
                ModBase.LogLevel.Feedback,
                userSummary: Lang.Text("Minecraft.Folder.Error.OperationFailed"));
        }
    }
}
