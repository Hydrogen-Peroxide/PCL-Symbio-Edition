using System;
using System.IO;
using System.Text;

namespace PCL;

internal static class ModBedrockRegistration
{
    private static readonly BedrockPackageRegistration Registration = new(script =>
        ModBase.ShellAndGetOutputChecked("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)), timeout: 300000));

    internal static bool IsRegisteredFromDirectory(string packageName, string directory) =>
        Registration.IsRegisteredFromDirectory(packageName, directory);

    internal static void UnregisterPortable(string directory)
    {
        var identity = BedrockPackageRegistration.ReadIdentity(Path.Combine(directory, "AppxManifest.xml"));
        if (identity is not null) Registration.UnregisterFromDirectory(identity, directory);
    }
}
