using PCL;
using System.Text;

var failures = 0;
var tests = 0;
var root = Path.Combine(Path.GetTempPath(), "PclBedrockRegression", "versions");

void Check(string name, Action test)
{
    tests++;
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
    }
}

void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (var id in new[] { "1.21.132", "1.21.132.0", "Minecraft Preview", "custom-package" })
{
    Check("accept version " + id, () =>
    {
        var path = ModBedrockUwp.GetVersionDirectory(root, id);
        Require(path == Path.Combine(root, id), "Valid version path changed.");
    });
}

foreach (var id in new[] { "", " ", ".", "..", @"..\outside", "../outside", @"nested\version",
             Path.GetPathRoot(root)!, @"C:\outside", "C:outside", "version.", "version ", "extracted", "EXTRACTED" })
{
    Check("reject unsafe version [" + id + "]", () =>
    {
        var rejected = false;
        try { ModBedrockUwp.GetVersionDirectory(root, id); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Unsafe version was accepted.");
    });
}

foreach (var result in new[] { "StillInstalled", "", "Access denied", "Cleared\nStillInstalled" })
{
    Check("abort unverified preserving uninstall [" + result + "]", () =>
    {
        ModBase.Commands.Clear();
        ModBase.Responses.Clear();
        ModBase.Responses.Enqueue("Error: preservation failed");
        ModBase.Responses.Enqueue(result);
        ModBase.Responses.Enqueue(""); // Old implementation uses this for its destructive fallback.
        var aborted = false;
        try { ModBedrockUwp.UninstallPreservingData(); }
        catch (InvalidOperationException) { aborted = true; }
        var removals = ModBase.Commands.Where(command => command.Contains("Remove-AppxPackage")).ToList();
        Require(removals.All(command => command.Contains("-PreserveApplicationData")),
            "Uninstall fell back to deleting application data.");
        Require(aborted, "Installation would continue without confirmed data preservation.");
        // 保留数据卸载（1 次）+ 安装状态校验（1 次）+ 签名类型查询（1 次）
        Require(ModBase.Commands.Count == 3, "Unexpected uninstall retry.");
    });
}

Check("continue after verified preserving uninstall", () =>
{
    ModBase.Commands.Clear();
    ModBase.Responses.Clear();
    ModBase.Responses.Enqueue("");
    ModBase.Responses.Enqueue("Cleared\r\n");
    ModBedrockUwp.UninstallPreservingData();
    Require(ModBase.Commands.Count == 2, "Unexpected command count.");
    Require(ModBase.Responses.Count == 0, "Uninstall verification was skipped.");
});

foreach (var path in new[] { @"C:\Games\Minecraft.appx", @"C:\O'Brien\Minecraft.appx", @"C:\$HOME\Minecraft.appx" })
{
    foreach (var register in new[] { false, true })
    {
        Check($"literal installation path [{path}], register={register}", () =>
        {
            var arguments = ModBedrockUwp.GetAppxInstallArguments(path, register);
            const string marker = "-EncodedCommand "; // 同时允许其前方的 -NonInteractive / -OutputFormat Text 等参数
            Require(arguments.StartsWith("-NoProfile ", StringComparison.Ordinal) &&
                    arguments.Contains(marker, StringComparison.Ordinal),
                "Script is exposed to command-line parsing.");
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(
                arguments[(arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]));
            var literal = "'" + path.Replace("'", "''") + "'";
            Require(script == $"Add-AppxPackage -{(register ? "Register" : "Path")} {literal} -ErrorAction Stop 2>&1",
                "Installation path is not a PowerShell string literal.");
        });
    }
}

Console.WriteLine($"{tests - failures}/{tests} passed");
return failures == 0 ? 0 : 1;
