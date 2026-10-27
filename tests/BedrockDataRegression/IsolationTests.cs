using PCL;

namespace BedrockDataRegression;

internal static class IsolationTests
{
    private const string SharedName = "Minecraft Bedrock";
    private const string BackupSuffix = ".pcl-shared";

    internal static void Run(RegressionSuite suite)
    {
        suite.Check("isolation creates and restores native junctions with literal percent and ampersand paths", directory =>
        {
            var appData = directory.MakeDirectory("appdata & 100%");
            var isolation = directory.At("isolation & 100%");
            var shared = directory.At("appdata & 100%", SharedName);
            directory.Write("appdata & 100%/" + SharedName + "/world.dat", "shared-world");
            // Exercise the production native junction API; no shell receives these paths.
            var manager = new BedrockIsolationManager(appData, isolation, () => false);
            var session = manager.Apply(shared, "Minecraft Preview & 100%");
            Assert.True(TestDirectory.IsJunction(shared), "Native junction creation failed.");
            Assert.Equal(Path.Combine(isolation, "Minecraft Preview & 100%"),
                new DirectoryInfo(shared).ResolveLinkTarget(false)?.FullName, "Special characters changed the junction target.");
            directory.Write("appdata & 100%/" + SharedName + "/isolated.dat", "isolated-world");
            manager.Restore(session);
            Assert.True(!TestDirectory.IsJunction(shared), "Native junction was not restored.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Special-character shared data was lost.");
            Assert.Equal("isolated-world", File.ReadAllText(Path.Combine(isolation, "Minecraft Preview & 100%", "isolated.dat")), "Special-character isolated data was lost.");
        });

        suite.Check("isolation restores the original shared directory", directory =>
        {
            var (manager, shared, _) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var session = manager.Apply(shared, "1.21.0");
            Assert.Equal(shared, session.SharedRoot, "Session refers to a different shared directory.");
            Assert.True(TestDirectory.IsJunction(shared), "Isolation did not create a junction.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Shared world was not backed up.");
            directory.Write("appdata/" + SharedName + "/isolated.dat", "isolated-world");

            manager.Restore(session);
            Assert.True(!TestDirectory.IsJunction(shared), "Own junction was not removed.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Shared world was not restored.");
            Assert.Equal("isolated-world", File.ReadAllText(directory.At("isolation", "1.21.0", "isolated.dat")), "Isolated world was deleted.");
            Assert.True(!Directory.Exists(shared + BackupSuffix), "Restored backup was left active.");
        });

        suite.Check("isolation restores a session without a preexisting shared directory", directory =>
        {
            var (manager, shared, _) = Create(directory);
            var session = manager.Apply(shared, "1.21.0");
            directory.Write("appdata/" + SharedName + "/isolated.dat", "isolated-world");
            manager.Restore(session);
            Assert.True(!TestDirectory.IsJunction(shared), "Own junction survived restore.");
            Assert.Equal("isolated-world", File.ReadAllText(directory.At("isolation", "1.21.0", "isolated.dat")), "Isolated data was lost.");
        });

        suite.Check("isolation recovers an owned session after manager recreation", directory =>
        {
            var (manager, shared, appData) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            manager.Apply(shared, "1.21.0");
            var restarted = new BedrockIsolationManager(appData, directory.At("isolation"), () => false, directory.CreateJunction);
            restarted.Restore();
            Assert.True(!TestDirectory.IsJunction(shared), "Valid local marker was not recovered.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Recovery lost the original world.");
        });

        suite.Check("isolation leaves an unowned junction untouched", directory =>
        {
            var (manager, shared, _) = Create(directory);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            directory.CreateJunction(shared, foreign);
            manager.Restore();
            Assert.True(TestDirectory.IsJunction(shared), "Restore removed an unowned junction.");
            Assert.Equal("foreign-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Unowned junction target changed.");
            Assert.Rejected(() => manager.Apply(shared, "1.21.0"));
            Assert.True(TestDirectory.IsJunction(shared), "Apply removed an unowned junction.");
            Assert.Equal("foreign-world", File.ReadAllText(directory.At("foreign", "world.dat")), "Apply changed unrelated data.");
        });

        suite.Check("isolation leaves an unowned junction and conflicting backup untouched", directory =>
        {
            var (manager, shared, _) = Create(directory);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            directory.Write("appdata/" + SharedName + BackupSuffix + "/world.dat", "unowned-backup");
            directory.CreateJunction(shared, foreign);
            manager.Restore();
            Assert.True(TestDirectory.IsJunction(shared), "A backup name alone established ownership.");
            Assert.Equal("unowned-backup", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Unowned backup changed.");
            Assert.Equal("foreign-world", File.ReadAllText(directory.At("foreign", "world.dat")), "Unowned target changed.");
        });

        suite.Check("isolation refuses an existing backup conflict", directory =>
        {
            var (manager, shared, _) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            directory.Write("appdata/" + SharedName + BackupSuffix + "/world.dat", "unowned-backup");
            Assert.Rejected(() => manager.Apply(shared, "1.21.0"));
            Assert.True(!TestDirectory.IsJunction(shared), "Conflicting backup allowed a directory replacement.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Shared world changed.");
            Assert.Equal("unowned-backup", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Existing backup changed.");
        });

        suite.Check("isolation rejects a shared directory outside injected appdata", directory =>
        {
            var (manager, _, _) = Create(directory);
            var unrelated = directory.MakeDirectory("unrelated");
            directory.Write("unrelated/world.dat", "unrelated-world");
            Assert.Rejected(() => manager.Apply(unrelated, "1.21.0"));
            Assert.Equal("unrelated-world", File.ReadAllText(directory.At("unrelated", "world.dat")), "Unrelated world changed.");
            Assert.True(!TestDirectory.IsJunction(unrelated), "Out-of-scope root was redirected.");
        });

        foreach (var versionId in new[] { "..", "../outside", @"..\outside", @"C:\outside", "file:stream", "CON", "version.", "version " })
        {
            suite.Check("isolation rejects unsafe version " + versionId, directory =>
            {
                var (manager, shared, _) = Create(directory);
                directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
                Assert.Rejected(() => manager.Apply(shared, versionId));
                Assert.True(!TestDirectory.IsJunction(shared), "Unsafe version changed the shared root.");
                Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Unsafe version lost the shared world.");
            });
        }

        suite.Check("isolation refuses changes while a game is running", directory =>
        {
            var appData = directory.MakeDirectory("appdata");
            var shared = directory.At("appdata", SharedName);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var manager = new BedrockIsolationManager(appData, directory.At("isolation"), () => true, directory.CreateJunction);
            Assert.Rejected(() => manager.Apply(shared, "1.21.0"));
            manager.Restore();
            Assert.True(!TestDirectory.IsJunction(shared), "Running game allowed redirection.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Running game lost its shared world.");
        });

        suite.Check("isolation defers restore until the game stops", directory =>
        {
            var running = false;
            var appData = directory.MakeDirectory("appdata");
            var shared = directory.At("appdata", SharedName);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var manager = new BedrockIsolationManager(appData, directory.At("isolation"), () => running, directory.CreateJunction);
            var session = manager.Apply(shared, "1.21.0");
            running = true;
            manager.Restore(session);
            Assert.True(TestDirectory.IsJunction(shared), "Restore removed an active game's junction.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Active session backup changed.");
            running = false;
            manager.Restore(session);
            Assert.True(!TestDirectory.IsJunction(shared), "Stopped session could not restore.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Deferred restore lost the shared world.");
        });

        suite.Check("isolation ignores a stale completion callback", directory =>
        {
            var (manager, shared, _) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var oldSession = manager.Apply(shared, "1.21.0");
            manager.Restore(oldSession);
            var currentSession = manager.Apply(shared, "1.21.1");
            manager.Restore(oldSession);
            Assert.True(TestDirectory.IsJunction(shared), "Stale callback removed the current junction.");
            var target = new DirectoryInfo(shared).ResolveLinkTarget(false)?.FullName;
            Assert.Equal(directory.At("isolation", "1.21.1"), target, "Stale callback changed the current target.");
            manager.Restore(currentSession);
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Current session did not restore.");
        });

        suite.Check("isolation refuses to remove a replaced junction", directory =>
        {
            var (manager, shared, _) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var session = manager.Apply(shared, "1.21.0");
            directory.Delete(shared);
            var foreign = directory.MakeDirectory("foreign");
            directory.Write("foreign/world.dat", "foreign-world");
            directory.CreateJunction(shared, foreign);
            manager.Restore(session);
            manager.Restore();
            Assert.True(TestDirectory.IsJunction(shared), "Restore removed a junction whose target no longer matched.");
            Assert.Equal("foreign-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Replaced target changed.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Original backup was overwritten.");
        });

        suite.Check("isolation preserves data when the ownership marker is damaged", directory =>
        {
            var (manager, shared, _) = Create(directory);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var session = manager.Apply(shared, "1.21.0");
            File.WriteAllText(shared + ".pcl-isolation.json", "{broken-json");
            manager.Restore(session);
            manager.Restore();
            Assert.True(TestDirectory.IsJunction(shared), "An invalid marker authorized junction removal.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared + BackupSuffix, "world.dat")), "Damaged marker lost the original backup.");
            Assert.Rejected(() => manager.Apply(shared, "1.21.1"));
            Assert.True(TestDirectory.IsJunction(shared), "Apply removed a link with an invalid marker.");
        });

        suite.Check("isolation rolls back a failed junction creation", directory =>
        {
            var appData = directory.MakeDirectory("appdata");
            var shared = directory.At("appdata", SharedName);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var manager = new BedrockIsolationManager(appData, directory.At("isolation"), () => false,
                (_, _) => throw new IOException("Injected junction creation failure."));
            Assert.Rejected(() => manager.Apply(shared, "1.21.0"));
            Assert.True(!TestDirectory.IsJunction(shared), "Failed apply left a junction.");
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Failed apply did not restore the shared world.");
        });

        suite.Check("isolation validates junction creation before reporting success", directory =>
        {
            var appData = directory.MakeDirectory("appdata");
            var shared = directory.At("appdata", SharedName);
            directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
            var manager = new BedrockIsolationManager(appData, directory.At("isolation"), () => false, (_, _) => { });
            Assert.Rejected(() => manager.Apply(shared, "1.21.0"));
            Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Incomplete apply lost the shared world.");
        });
    }

    private static (BedrockIsolationManager Manager, string Shared, string AppData) Create(TestDirectory directory)
    {
        var appData = directory.MakeDirectory("appdata");
        var manager = new BedrockIsolationManager(appData, directory.At("isolation"), () => false, directory.CreateJunction);
        return (manager, directory.At("appdata", SharedName), appData);
    }
}
