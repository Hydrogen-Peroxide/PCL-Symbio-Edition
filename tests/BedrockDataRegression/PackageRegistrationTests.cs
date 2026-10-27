using System.Xml.Linq;
using PCL;

namespace BedrockDataRegression;

internal static class PackageRegistrationTests
{
    private const string Identity = "Microsoft.MinecraftUWP";
    private const string OwnedFullName = "Microsoft.MinecraftUWP_owned-fixture";

    internal static void Run(RegressionSuite suite)
    {
        foreach (var identity in new[] { Identity, "Microsoft.MinecraftWindowsBeta", "custom-package_1.0" })
        {
            suite.Check("registration reads manifest identity " + identity, directory =>
            {
                var manifest = WriteManifest(directory, identity);
                Assert.Equal(identity, BedrockPackageRegistration.ReadIdentity(manifest), "Manifest identity changed.");
            });
        }

        foreach (var identity in new[] { "", " ", "bad'name", "bad;name", "bad$name", "bad/name", "bad*name", "-invalid", "bad\nname", "中文包名" })
        {
            suite.Check("registration rejects identity " + identity.Replace("\n", "\\n"), directory =>
            {
                var manifest = WriteManifest(directory, identity);
                Assert.Rejected(() => BedrockPackageRegistration.ReadIdentity(manifest));
                var executed = false;
                var registration = new BedrockPackageRegistration(_ => { executed = true; return "Owned"; });
                Assert.Rejected(() => registration.IsRegisteredFromDirectory(identity, directory.At("game")));
                Assert.Rejected(() => registration.UnregisterFromDirectory(identity, directory.At("game")));
                Assert.True(!executed, "Unsafe identity reached PowerShell execution.");
            });
        }

        suite.Check("registration handles a missing manifest", directory =>
            Assert.True(BedrockPackageRegistration.ReadIdentity(directory.At("missing.xml")) is null, "Missing manifest returned an identity."));

        suite.Check("registration rejects a manifest without Identity", directory =>
        {
            directory.Write("AppxManifest.xml", "<Package />");
            Assert.Rejected(() => BedrockPackageRegistration.ReadIdentity(directory.At("AppxManifest.xml")));
        });

        foreach (var (output, expected) in new[] { ("Owned", true), ("Absent", false), (" Owned\r\n", true) })
        {
            suite.Check("registration accepts verified query output " + output.Trim(), directory =>
            {
                var registration = new BedrockPackageRegistration(_ => output);
                Assert.Equal(expected, registration.IsRegisteredFromDirectory(Identity, directory.At("game")), "Verified query status was misread.");
            });
        }

        foreach (var output in new[] { "", "Unknown", "Access denied", "Owned\nAbsent", "Cleared" })
        {
            suite.Check("registration rejects unverified query output [" + output.Replace("\n", "\\n") + "]", directory =>
            {
                var registration = new BedrockPackageRegistration(_ => output);
                Assert.Rejected(() => registration.IsRegisteredFromDirectory(Identity, directory.At("game")));
            });
        }

        foreach (var output in new[] { "", "Owned", "Absent", "Access denied", "Cleared\nOwned" })
        {
            suite.Check("registration rejects unverified removal output [" + output.Replace("\n", "\\n") + "]", directory =>
            {
                var registration = new BedrockPackageRegistration(_ => output);
                Assert.Rejected(() => registration.UnregisterFromDirectory(Identity, directory.At("game")));
            });
        }

        suite.Check("registration propagates checked-command failures", directory =>
        {
            var registration = new BedrockPackageRegistration(_ => throw new IOException("Injected checked-command failure."));
            Assert.Rejected(() => registration.IsRegisteredFromDirectory(Identity, directory.At("game")));
            Assert.Rejected(() => registration.UnregisterFromDirectory(Identity, directory.At("game")));
        });

        suite.Check("registration executes literal single-quote dollar percent and ampersand paths", directory =>
        {
            var game = directory.MakeDirectory("O'Brien $USERPROFILE %TEMP% & Minecraft");
            var fixture = new PowerShellPackageFixture([Owned(game)]);
            var registration = new BedrockPackageRegistration(fixture.Execute);
            Assert.True(registration.IsRegisteredFromDirectory(Identity, game), "Literal path was expanded or truncated in the generated script.");
            Assert.Equal(0, fixture.LastResult!.Removed.Length, "Registration query removed a package.");
            registration.UnregisterFromDirectory(Identity, game);
            AssertSuccessfulRemoval(fixture, 1);
            Assert.True(Directory.Exists(game), "Registration helper deleted game files.");
        });

        suite.Check("registration query reports absent for an empty package list", directory =>
        {
            var fixture = new PowerShellPackageFixture([]);
            var registration = new BedrockPackageRegistration(fixture.Execute);
            Assert.True(!registration.IsRegisteredFromDirectory(Identity, directory.At("game")), "Empty package list reported ownership.");
            Assert.Equal(0, fixture.LastResult!.Removed.Length, "Empty query attempted removal.");
        });

        suite.Check("registration unregisters no packages when the target is absent", directory =>
        {
            var fixture = new PowerShellPackageFixture([]);
            new BedrockPackageRegistration(fixture.Execute).UnregisterFromDirectory(Identity, directory.At("game"));
            AssertSuccessfulRemoval(fixture, 0);
            Assert.Equal(0, fixture.LastResult!.Remaining.Length, "Absent unregister invented a package.");
        });

        suite.Check("registration leaves a same-identity store package at another location untouched", directory =>
        {
            var game = directory.MakeDirectory("game");
            var store = new PackageFixture(Identity, "store-fixture", directory.At("store"));
            var fixture = new PowerShellPackageFixture([store]);
            var registration = new BedrockPackageRegistration(fixture.Execute);
            Assert.True(!registration.IsRegisteredFromDirectory(Identity, game), "A store package was treated as this portable directory.");
            registration.UnregisterFromDirectory(Identity, game);
            AssertSuccessfulRemoval(fixture, 0);
            Assert.Equal(store.PackageFullName, fixture.LastResult!.Remaining.Single().PackageFullName, "Store package was removed.");
        });

        suite.Check("registration removes only the exact identity and InstallLocation match", directory =>
        {
            var game = directory.MakeDirectory("game");
            PackageFixture[] packages =
            [
                Owned(game),
                new(Identity, "store-fixture", directory.At("store")),
                new(Identity, "nested-location-fixture", Path.Combine(game, "nested")),
                new(Identity, "prefix-location-fixture", game + "-other"),
                new(Identity, "unknown-location-fixture", ""),
                new("Other.Package", "other-identity-fixture", game)
            ];
            var fixture = new PowerShellPackageFixture(packages);
            new BedrockPackageRegistration(fixture.Execute).UnregisterFromDirectory(Identity, game);
            AssertSuccessfulRemoval(fixture, 1);
            Assert.Equal(OwnedFullName, fixture.LastResult!.Removed.Single().PackageFullName, "Unregister selected the wrong package.");
            Assert.True(fixture.LastResult.Remaining.Select(package => package.PackageFullName).Order().SequenceEqual(
                packages.Skip(1).Select(package => package.PackageFullName).Order()), "Unregister changed unrelated packages.");
        });

        suite.Check("registration compares Windows locations without case or trailing-slash differences", directory =>
        {
            var game = directory.MakeDirectory("game");
            var fixture = new PowerShellPackageFixture([Owned(game.ToUpperInvariant() + "\\")]);
            var registration = new BedrockPackageRegistration(fixture.Execute);
            Assert.True(registration.IsRegisteredFromDirectory(Identity, game), "Equivalent Windows location was not recognized.");
            registration.UnregisterFromDirectory(Identity, game);
            AssertSuccessfulRemoval(fixture, 1);
        });

        suite.Check("registration preserves target registration when removal fails", directory =>
        {
            var game = directory.MakeDirectory("game");
            directory.Write("game/world.dat", "keep-world");
            var fixture = new PowerShellPackageFixture([Owned(game)], "Remove");
            Assert.Rejected(() => new BedrockPackageRegistration(fixture.Execute).UnregisterFromDirectory(Identity, game));
            Assert.Equal(OwnedFullName, fixture.LastResult!.Remaining.Single().PackageFullName, "Failed removal lost registration.");
            Assert.True(fixture.LastResult.Removed.Single().PreserveApplicationData, "Failed removal omitted data preservation.");
            Assert.Equal("keep-world", File.ReadAllText(Path.Combine(game, "world.dat")), "Failed removal changed game data.");
        });

        suite.Check("registration requires the post-removal query to confirm Cleared", directory =>
        {
            var game = directory.MakeDirectory("game");
            var fixture = new PowerShellPackageFixture([Owned(game)], "KeepRegistration");
            Assert.Rejected(() => new BedrockPackageRegistration(fixture.Execute).UnregisterFromDirectory(Identity, game));
            Assert.Equal(2, fixture.LastResult!.QueryCount, "Unregister skipped the verification query.");
            Assert.Equal(OwnedFullName, fixture.LastResult.Remaining.Single().PackageFullName, "Mock unexpectedly removed the retained registration.");
            Assert.True(!fixture.LastResult.Output.Contains("Cleared"), "Still-owned directory was reported Cleared.");
        });

        suite.Check("registration query errors prevent all removal", directory =>
        {
            var game = directory.MakeDirectory("game");
            var fixture = new PowerShellPackageFixture([Owned(game)], "Query");
            var registration = new BedrockPackageRegistration(fixture.Execute);
            Assert.Rejected(() => registration.IsRegisteredFromDirectory(Identity, game));
            Assert.Rejected(() => registration.UnregisterFromDirectory(Identity, game));
            Assert.Equal(0, fixture.LastResult!.Removed.Length, "Query failure proceeded to removal.");
            Assert.Equal(OwnedFullName, fixture.LastResult.Remaining.Single().PackageFullName, "Query failure changed registration.");
        });
    }

    private static string WriteManifest(TestDirectory directory, string identity)
    {
        var path = directory.At("AppxManifest.xml");
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        new XDocument(new XElement(ns + "Package", new XElement(ns + "Identity", new XAttribute("Name", identity)))).Save(path);
        return path;
    }

    private static PackageFixture Owned(string directory) => new(Identity, OwnedFullName, directory);

    private static void AssertSuccessfulRemoval(PowerShellPackageFixture fixture, int expectedCount)
    {
        var result = fixture.LastResult ?? throw new InvalidOperationException("Package script did not run.");
        Assert.Equal("Cleared", result.Output.Single(), "Generated unregister script did not verify completion.");
        Assert.Equal(2, result.QueryCount, "Unregister did not query before and after removal.");
        Assert.Equal(expectedCount, result.Removed.Length, "Unexpected package removal count.");
        Assert.True(result.Removed.All(removal => removal.PreserveApplicationData && removal.ErrorAction == "Stop"),
            "Generated unregister script omitted data preservation or strict errors.");
    }
}
