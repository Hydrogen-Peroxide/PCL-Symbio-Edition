using System;
using System.IO;
using System.Threading;

namespace PCL;

internal static class ModBedrockInstall
{
    internal static string InstallGdk(string packagePath, string versionsDirectory, string versionId,
        Action<string, int, int>? onProgress = null, CancellationToken token = default)
    {
        var installRoot = Path.Combine(versionsDirectory, "extracted");
        return BedrockInstallTransaction.Install(installRoot, versionId,
            (staging, cancellation) => ModBedrockGdk.Extract(packagePath, staging, onProgress, cancellation),
            ModBedrockGdk.IsInstalled, ModBedrockIsolation.IsAnyGameRunning, token);
    }
}
