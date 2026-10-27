using PCL;

namespace BedrockDataRegression;

internal static class FileSystemTests
{
    internal static void Run(RegressionSuite suite)
    {
        foreach (var relativePath in new[] { "Minecraft.Windows.exe", "Content/data.bin", @"Content\data.bin", "Minecraft Preview/config.json" })
        {
            suite.Check("path accepts " + relativePath, directory =>
            {
                var root = directory.MakeDirectory("extract");
                var result = BedrockFileSystem.GetChildPath(root, relativePath);
                var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                Assert.Equal(Path.Combine(root, normalized), result, "Safe relative path changed.");
                TestDirectory.RequireContained(root, result);
            });
        }

        foreach (var relativePath in new[]
                 {
                     "", " ", ".", "..", "../outside.txt", @"..\outside.txt", @"Content\..\outside.txt",
                     @"C:\outside.txt", "C:outside.txt", @"\\server\share\outside.txt", "//server/share/outside.txt",
                     @"\outside.txt", "/outside.txt", "file:stream", "file::$DATA", "folder.", "folder ",
                     "CON.txt", "prn", "AUX.json", "nul", "COM1.bin", "lpt9", "dir//file", @"dir\\file",
                     "dir/?file", "dir/*file", "dir/\"file", "dir/fi|le", "dir/fi<le", "dir/fi>le", "file\0", "file\n"
                 })
        {
            suite.Check("path rejects " + relativePath.Replace("\0", "\\0").Replace("\n", "\\n"), directory =>
            {
                var root = directory.MakeDirectory("extract");
                directory.Write("outside.txt", "outside-data");
                Assert.Rejected(() => BedrockFileSystem.GetChildPath(root, relativePath));
                Assert.Equal("outside-data", File.ReadAllText(directory.At("outside.txt")), "Path validation modified unrelated data.");
            });
        }

        suite.Check("path rejects a child junction", directory =>
        {
            var root = directory.MakeDirectory("extract");
            var target = directory.MakeDirectory("outside");
            directory.Write("outside/victim.txt", "outside-data");
            directory.CreateJunction(directory.At("extract", "Content"), target);
            Assert.Rejected(() => BedrockFileSystem.GetChildPath(root, "Content/victim.txt"));
            Assert.Equal("outside-data", File.ReadAllText(directory.At("outside", "victim.txt")), "Junction target changed.");
        });

        suite.Check("path rejects a junction as extraction root", directory =>
        {
            var target = directory.MakeDirectory("outside");
            var root = directory.At("extract");
            directory.CreateJunction(root, target);
            Assert.Rejected(() => BedrockFileSystem.GetChildPath(root, "victim.txt"));
            Assert.True(!File.Exists(directory.At("outside", "victim.txt")), "Root junction target changed.");
        });

        suite.Check("path rejects a dangling junction", directory =>
        {
            var root = directory.MakeDirectory("extract");
            var target = directory.MakeDirectory("outside");
            directory.CreateJunction(directory.At("extract", "Content"), target);
            directory.Delete(target);
            Assert.Rejected(() => BedrockFileSystem.GetChildPath(root, "Content/victim.txt"));
            Assert.True(!Directory.Exists(target), "Validation recreated a dangling junction target.");
        });
    }
}
