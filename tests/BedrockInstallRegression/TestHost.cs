using System.Diagnostics;

// Only compiled into this regression executable. Never run real PowerShell or game processes.
namespace PCL;

internal static class ModBase
{
    internal enum LogLevel { Normal }
    internal static readonly Queue<string> Responses = new();
    internal static readonly List<string> Commands = [];

    public static void Log(string message, LogLevel level = LogLevel.Normal) { }

    public static string ShellAndGetOutput(string fileName, string arguments = "", int timeout = 1000000)
    {
        Commands.Add(arguments);
        if (fileName != "powershell.exe" || Responses.Count == 0)
            throw new InvalidOperationException("Unexpected shell command: " + arguments);
        return Responses.Dequeue();
    }

    public static string ShellAndGetOutputChecked(string fileName, string arguments = "", int timeout = 1000000,
        string? workingDirectory = null) => ShellAndGetOutput(fileName, arguments, timeout);
}

internal sealed class Process
{
    public static Process[] GetProcessesByName(string name) => [];
    public static Process? Start(ProcessStartInfo info) => throw new InvalidOperationException("Unexpected process launch.");
    public void Kill() => throw new InvalidOperationException("Unexpected process termination.");
    public bool WaitForExit(int milliseconds) => throw new InvalidOperationException("Unexpected process wait.");
}
