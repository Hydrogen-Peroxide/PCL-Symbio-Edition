using System.Diagnostics;

namespace BedrockDataRegression;

internal sealed class RegressionSuite : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PclBedrockDataRegression_" + Guid.NewGuid().ToString("N"));
    private int _tests;
    private int _failures;

    internal RegressionSuite()
    {
        TestDirectory.RequireContained(Path.GetTempPath(), _root);
        Directory.CreateDirectory(_root);
    }

    internal void Check(string name, Action<TestDirectory> test)
    {
        _tests++;
        try
        {
            using (var directory = new TestDirectory(_root, "case_" + _tests.ToString("D3")))
                test(directory);
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + ex);
        }
    }

    internal int Finish()
    {
        Console.WriteLine($"{_tests - _failures}/{_tests} passed");
        return _failures == 0 ? 0 : 1;
    }

    public void Dispose()
    {
        TestDirectory.RequireContained(Path.GetTempPath(), _root);
        TestDirectory.DeleteTree(_root, _root);
    }
}

internal sealed class TestDirectory : IDisposable
{
    private readonly string _suiteRoot;
    internal string Root { get; }

    internal TestDirectory(string suiteRoot, string name)
    {
        _suiteRoot = Path.GetFullPath(suiteRoot);
        Root = Path.GetFullPath(Path.Combine(_suiteRoot, name));
        RequireContained(_suiteRoot, Root);
        Directory.CreateDirectory(Root);
    }

    internal string At(params string[] components)
    {
        var path = Path.GetFullPath(Path.Combine(Root, Path.Combine(components)));
        RequireContained(Root, path);
        return path;
    }

    internal string MakeDirectory(params string[] components)
    {
        var path = At(components);
        Directory.CreateDirectory(path);
        return path;
    }

    internal void Write(string relativePath, string content)
    {
        var path = At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    internal void CreateJunction(string linkPath, string targetPath)
    {
        RequireContained(Root, linkPath);
        RequireContained(Root, targetPath);
        // cmd.exe receives only validated paths under this test's GUID temporary root.
        foreach (var path in new[] { linkPath, targetPath })
            if (path.IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) >= 0)
                throw new InvalidOperationException("Unsafe temporary junction path.");

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo) ?? throw new IOException("Could not start temporary junction creation.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Temporary junction creation timed out.");
        }
        Task.WaitAll(output, error);
        Assert.True(process.ExitCode == 0 && IsJunction(linkPath), "Temporary junction creation failed: " + output.Result + error.Result);
    }

    internal void Delete(string path) => DeleteTree(Root, path);

    internal static bool IsJunction(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static void RequireContained(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test operation escaped its temporary root: " + fullPath);
    }

    internal static void DeleteTree(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) RequireContained(fullRoot, fullPath);

        FileAttributes attributes;
        try { attributes = File.GetAttributes(fullPath); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }

        // Never recurse through links, including links whose targets no longer exist.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(fullPath);
            else File.Delete(fullPath);
            return;
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
            File.Delete(fullPath);
            return;
        }
        foreach (var child in Directory.EnumerateFileSystemEntries(fullPath)) DeleteTree(fullRoot, child);
        Directory.Delete(fullPath);
    }

    public void Dispose() => DeleteTree(_suiteRoot, Root);
}

internal static class Assert
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}");
    }

    internal static void Rejected(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Unsafe operation was accepted.");
    }

    internal static void Cancelled(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancelled operation completed.");
    }
}
