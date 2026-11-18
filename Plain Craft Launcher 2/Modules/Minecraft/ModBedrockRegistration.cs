using System;
using System.IO;
using System.Text;

namespace PCL;

internal static class ModBedrockRegistration
{
    private static readonly BedrockPackageRegistration Registration = new(script =>
    {
        // 只取 stdout：PowerShell 会把模块加载进度以 CLIXML 写到 stderr，
        // 混进 "Owned"/"Absent" 里会让注册结果永远无法确认（表现为“切换成功但校验失败”）
        var result = ModBase.ShellAndGetResult("powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)), timeout: 300000);
        if (!result.Succeeded) throw new ModBase.ProcessExecutionException(result);
        return result.StandardOutput;
    });

    internal static bool IsRegisteredFromDirectory(string packageName, string directory) =>
        Registration.IsRegisteredFromDirectory(packageName, directory);

    internal static void UnregisterPortable(string directory)
    {
        var identity = BedrockPackageRegistration.ReadIdentity(Path.Combine(directory, "AppxManifest.xml"));
        if (identity is not null) Registration.UnregisterFromDirectory(identity, directory);
    }
}
