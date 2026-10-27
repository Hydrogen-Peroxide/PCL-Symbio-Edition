using PCL;

namespace BedrockDataRegression;

internal static class LaunchLeaseTests
{
    private const string SharedName = "Minecraft Bedrock";

    internal static void Run(RegressionSuite suite)
    {
        foreach (var reserveLaunch in new[] { false, true })
        foreach (var version in new[] { "version-A", "version-B" })
        {
            suite.Check($"pending launch blocks another manager's {(reserveLaunch ? "reservation" : "preparation")} for {version}", directory =>
            {
                var (owner, other, shared) = Create(directory, () => false);
                var session = owner.Apply(shared, "version-A", reserveLaunch: true);
                Assert.Rejected(() => other.Apply(shared, version, reserveLaunch));
                AssertCurrentTarget(directory, shared, "version-A");
                Assert.True(owner.Restore(session), "Owning session could not clean up a failed launch.");
                AssertSharedData(shared);
            });
        }

        suite.Check("pending launch rejects generic restore but allows its explicit failure cleanup", directory =>
        {
            var (owner, other, shared) = Create(directory, () => false);
            var session = owner.Apply(shared, "version-A", reserveLaunch: true);
            Assert.True(!owner.Restore(), "Owner's generic startup restore removed a pending launch.");
            Assert.True(!other.Restore(), "Another manager removed a pending launch.");
            AssertCurrentTarget(directory, shared, "version-A");
            Assert.True(owner.Restore(session), "Explicit failed-launch cleanup was refused.");
            AssertSharedData(shared);
        });

        suite.Check("launched session uses game-running protection until exit", directory =>
        {
            var running = false;
            var (owner, other, shared) = Create(directory, () => running);
            var session = owner.Apply(shared, "version-A", reserveLaunch: true);
            running = true;
            owner.MarkLaunched(session);
            Assert.Rejected(() => other.Apply(shared, "version-B"));
            Assert.Rejected(() => other.Apply(shared, "version-B", reserveLaunch: true));
            Assert.True(!other.Restore(), "Generic restore changed a running session.");
            Assert.True(!owner.Restore(session), "Completion restore changed a running session.");
            AssertCurrentTarget(directory, shared, "version-A");
            running = false;
            Assert.True(owner.Restore(session), "Stopped launched session could not restore.");
            AssertSharedData(shared);
        });

        suite.Check("MarkLaunched releases pending protection when no game is running", directory =>
        {
            var (owner, other, shared) = Create(directory, () => false);
            var session = owner.Apply(shared, "version-A", reserveLaunch: true);
            owner.MarkLaunched(session);
            Assert.True(other.Restore(), "MarkLaunched did not clear the pending reservation.");
            AssertSharedData(shared);
        });

        suite.Check("stale session cannot restore or mark a newer pending launch", directory =>
        {
            var (owner, other, shared) = Create(directory, () => false);
            var old = owner.Apply(shared, "version-A");
            var current = other.Apply(shared, "version-B", reserveLaunch: true);
            owner.Restore(old);
            owner.MarkLaunched(old);
            AssertCurrentTarget(directory, shared, "version-B");
            Assert.True(!owner.Restore(), "Stale MarkLaunched cleared a newer pending reservation.");
            Assert.Rejected(() => owner.Apply(shared, "version-C", reserveLaunch: true));
            Assert.True(other.Restore(current), "Current failed-launch cleanup was refused.");
            AssertSharedData(shared);
        });
    }

    private static (BedrockIsolationManager Owner, BedrockIsolationManager Other, string Shared) Create(
        TestDirectory directory, Func<bool> running)
    {
        var appData = directory.MakeDirectory("appdata");
        var isolation = directory.At("isolation");
        directory.Write("appdata/" + SharedName + "/world.dat", "shared-world");
        return (
            new BedrockIsolationManager(appData, isolation, running, directory.CreateJunction),
            new BedrockIsolationManager(appData, isolation, running, directory.CreateJunction),
            directory.At("appdata", SharedName));
    }

    private static void AssertCurrentTarget(TestDirectory directory, string shared, string version)
    {
        Assert.True(TestDirectory.IsJunction(shared), "Current launch junction was removed.");
        Assert.Equal(directory.At("isolation", version), new DirectoryInfo(shared).ResolveLinkTarget(false)?.FullName,
            "Current launch target changed.");
        Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared + ".pcl-shared", "world.dat")), "Current launch backup changed.");
    }

    private static void AssertSharedData(string shared)
    {
        Assert.True(!TestDirectory.IsJunction(shared), "Session restore left a junction.");
        Assert.Equal("shared-world", File.ReadAllText(Path.Combine(shared, "world.dat")), "Session restore lost shared data.");
    }
}
