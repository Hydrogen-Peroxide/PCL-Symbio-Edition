using PCL;

namespace BedrockDataRegression;

internal static class InstallTransactionTests
{
    private const string Version = "1.21.0";
    private const string Executable = "Minecraft.Windows.exe";
    private static readonly string[] UserDirectories = ["mods", "preload", "plugins", "config", "configs", "logs"];

    internal static void Run(RegressionSuite suite)
    {
        suite.Check("install commits a validated new version", directory =>
        {
            var root = directory.At("extracted");
            var result = BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                TestDirectory.RequireContained(root, staging);
                Assert.True(!staging.Equals(Path.Combine(root, Version), StringComparison.OrdinalIgnoreCase), "Extraction wrote directly into the final directory.");
                WritePayload(staging, "new-binary");
            }, Validate);
            Assert.Equal(Path.Combine(root, Version), result, "Install returned a different version directory.");
            Assert.Equal("new-binary", File.ReadAllText(Path.Combine(result, Executable)), "New binary was not committed.");
            AssertNoTemporaryContent(root);
        });

        suite.Check("install replaces the binary and preserves user directories and backup", directory =>
        {
            var root = SeedOldVersion(directory);
            var result = BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "new-binary");
                foreach (var name in UserDirectories)
                {
                    Directory.CreateDirectory(Path.Combine(staging, name));
                    File.WriteAllText(Path.Combine(staging, name, "user.txt"), "package-default");
                }
            }, Validate);
            Assert.Equal("new-binary", File.ReadAllText(Path.Combine(result, Executable)), "Old binary survived successful replacement.");
            foreach (var name in UserDirectories)
                Assert.Equal("user-" + name, File.ReadAllText(Path.Combine(result, name, "user.txt")), "User directory was not preserved: " + name);
            var backup = FindBackup(root);
            Assert.Equal("old-binary", File.ReadAllText(Path.Combine(backup, Executable)), "Previous binary was not retained in backup.");
            Assert.Equal("unknown-user-data", File.ReadAllText(Path.Combine(backup, "custom-worlds", "world.dat")), "Backup discarded user data outside the migration list.");
            foreach (var name in UserDirectories)
                Assert.Equal("user-" + name, File.ReadAllText(Path.Combine(backup, name, "user.txt")), "Backup lost a user directory: " + name);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install preserves the old version when extraction fails", directory =>
        {
            var root = SeedOldVersion(directory);
            var validated = false;
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "partial-binary");
                throw new IOException("Injected extraction failure.");
            }, _ => { validated = true; return true; }));
            Assert.True(!validated, "Failed extraction proceeded to validation.");
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install rejects a payload without its executable", directory =>
        {
            var root = SeedOldVersion(directory);
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                Directory.CreateDirectory(staging);
                File.WriteAllText(Path.Combine(staging, "partial.dat"), "incomplete");
            }, Validate));
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install cleanup never follows a junction inside staging", directory =>
        {
            var root = SeedOldVersion(directory);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "partial-binary");
                directory.CreateJunction(Path.Combine(staging, "unexpected-link"), foreign);
                throw new IOException("Injected failure with a staged junction.");
            }, Validate));
            AssertOldVersion(root);
            Assert.Equal("foreign-world", File.ReadAllText(directory.At("foreign", "world.dat")), "Staging cleanup traversed a junction.");
            AssertNoTemporaryContent(root);
        });

        suite.Check("install cleanup removes only the link when staging is replaced by a junction", directory =>
        {
            var root = SeedOldVersion(directory);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                directory.Delete(staging);
                directory.CreateJunction(staging, foreign);
                throw new IOException("Injected failure after staging replacement.");
            }, Validate));
            AssertOldVersion(root);
            Assert.Equal("foreign-world", File.ReadAllText(directory.At("foreign", "world.dat")), "Staging-root cleanup traversed a junction.");
            AssertNoTemporaryContent(root);
        });

        suite.Check("install preserves the old version when validation throws", directory =>
        {
            var root = SeedOldVersion(directory);
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version,
                (staging, _) => WritePayload(staging, "new-binary"),
                _ => throw new IOException("Injected validation failure.")));
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install observes cancellation before extraction", directory =>
        {
            var root = SeedOldVersion(directory);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var extracted = false;
            Assert.Cancelled(() => BedrockInstallTransaction.Install(root, Version,
                (_, _) => extracted = true, Validate, token: cancellation.Token));
            Assert.True(!extracted, "Pre-cancelled install started extracting.");
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install preserves the old version when extraction is cancelled", directory =>
        {
            var root = SeedOldVersion(directory);
            using var cancellation = new CancellationTokenSource();
            Assert.Cancelled(() => BedrockInstallTransaction.Install(root, Version, (staging, token) =>
            {
                WritePayload(staging, "partial-binary");
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }, Validate, token: cancellation.Token));
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install rechecks cancellation before committing", directory =>
        {
            var root = SeedOldVersion(directory);
            using var cancellation = new CancellationTokenSource();
            Assert.Cancelled(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "new-binary");
                cancellation.Cancel();
            }, Validate, token: cancellation.Token));
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install leaves no final version after a failed first install", directory =>
        {
            var root = directory.At("extracted");
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "partial-binary");
                throw new IOException("Injected first-install failure.");
            }, Validate));
            Assert.True(!Directory.Exists(Path.Combine(root, Version)), "Failed first install left a launchable version.");
            AssertNoTemporaryContent(root);
        });

        suite.Check("install refuses extraction while a game is running", directory =>
        {
            var root = SeedOldVersion(directory);
            var extracted = false;
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version,
                (_, _) => extracted = true, Validate, () => true));
            Assert.True(!extracted, "Running game did not stop extraction.");
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        suite.Check("install refuses commit if a game starts during extraction", directory =>
        {
            var root = SeedOldVersion(directory);
            var running = false;
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version, (staging, _) =>
            {
                WritePayload(staging, "new-binary");
                running = true;
            }, Validate, () => running));
            AssertOldVersion(root);
            AssertNoTemporaryContent(root);
        });

        foreach (var versionId in new[] { "..", "../outside", @"..\outside", @"C:\outside", "file:stream", "CON", "version.", "version " })
        {
            suite.Check("install rejects unsafe version " + versionId, directory =>
            {
                var root = SeedOldVersion(directory);
                var extracted = false;
                Assert.Rejected(() => BedrockInstallTransaction.Install(root, versionId,
                    (_, _) => extracted = true, Validate));
                Assert.True(!extracted, "Unsafe version reached extraction.");
                AssertOldVersion(root);
                AssertNoTemporaryContent(root);
            });
        }

        suite.Check("install refuses a junction as the existing version", directory =>
        {
            var root = directory.MakeDirectory("extracted");
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/" + Executable, "foreign-binary");
            directory.CreateJunction(Path.Combine(root, Version), foreign);
            var extracted = false;
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version,
                (_, _) => extracted = true, Validate));
            Assert.True(!extracted, "Existing junction reached extraction.");
            Assert.True(TestDirectory.IsJunction(Path.Combine(root, Version)), "Existing version junction was removed.");
            Assert.Equal("foreign-binary", File.ReadAllText(directory.At("foreign", Executable)), "Unrelated junction target changed.");
        });

        suite.Check("install refuses a junction as the backup directory", directory =>
        {
            var root = SeedOldVersion(directory);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            directory.CreateJunction(Path.Combine(root, ".backups"), foreign);
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version,
                (staging, _) => WritePayload(staging, "new-binary"), Validate));
            AssertOldVersion(root);
            Assert.Equal("foreign-world", File.ReadAllText(directory.At("foreign", "world.dat")), "Backup junction target changed.");
            Assert.Equal(1, Directory.EnumerateFileSystemEntries(foreign).Count(), "Backup was written through an unrelated junction.");
        });

        suite.Check("install refuses user data that redirects outside the version", directory =>
        {
            var root = SeedOldVersion(directory);
            directory.Delete(Path.Combine(root, Version, "mods"));
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/user.txt", "foreign-mod");
            directory.CreateJunction(Path.Combine(root, Version, "mods"), foreign);
            Assert.Rejected(() => BedrockInstallTransaction.Install(root, Version,
                (staging, _) => WritePayload(staging, "new-binary"), Validate));
            Assert.Equal("old-binary", File.ReadAllText(Path.Combine(root, Version, Executable)), "Unsafe user data replaced the old binary.");
            Assert.True(TestDirectory.IsJunction(Path.Combine(root, Version, "mods")), "Unsafe user data junction was removed.");
            Assert.Equal("foreign-mod", File.ReadAllText(directory.At("foreign", "user.txt")), "Unrelated mod data changed.");
            AssertNoTemporaryContent(root);
        });
    }

    private static string SeedOldVersion(TestDirectory directory)
    {
        directory.Write("extracted/" + Version + "/" + Executable, "old-binary");
        foreach (var name in UserDirectories)
            directory.Write("extracted/" + Version + "/" + name + "/user.txt", "user-" + name);
        directory.Write("extracted/" + Version + "/custom-worlds/world.dat", "unknown-user-data");
        return directory.At("extracted");
    }

    private static void WritePayload(string staging, string content)
    {
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, Executable), content);
    }

    private static bool Validate(string staging) => File.Exists(Path.Combine(staging, Executable));

    private static void AssertOldVersion(string root)
    {
        Assert.Equal("old-binary", File.ReadAllText(Path.Combine(root, Version, Executable)), "Old version was modified before commit.");
        foreach (var name in UserDirectories)
            Assert.Equal("user-" + name, File.ReadAllText(Path.Combine(root, Version, name, "user.txt")), "Existing user data changed: " + name);
        Assert.Equal("unknown-user-data", File.ReadAllText(Path.Combine(root, Version, "custom-worlds", "world.dat")), "Unknown user data changed before commit.");
    }

    private static string FindBackup(string root)
    {
        var parent = Path.Combine(root, ".backups", Version);
        Assert.True(Directory.Exists(parent), "Previous version backup directory is missing.");
        var backups = Directory.GetDirectories(parent);
        Assert.Equal(1, backups.Length, "Expected exactly one retained previous version.");
        return backups[0];
    }

    private static void AssertNoTemporaryContent(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(path) is Version or ".backups") continue;
            Assert.True(Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any(), "Temporary installation content survived: " + path);
        }
    }
}
