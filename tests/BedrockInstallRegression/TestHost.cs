using System.Diagnostics;

// Only compiled into this regression executable. Never run real PowerShell or game processes.
namespace PCL;

internal static class ModBase
{
    internal enum LogLevel { Normal }
    internal static readonly Queue<string> Responses = new();
    internal static readonly List<string> Commands = [];

    public static void Log(string message, LogLevel level = LogLevel.Normal) { }

    public sealed record ProcessExecutionResult(int ExitCode, bool TimedOut, string StandardOutput, string StandardError)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;
        public string Output => StandardOutput + StandardError;
    }

    public static ProcessExecutionResult ShellAndGetResult(string fileName, string arguments = "", int timeout = 1000000,
        string? workingDirectory = null)
    {
        Commands.Add(arguments);
        if (fileName != "powershell.exe" || Responses.Count == 0)
            throw new InvalidOperationException("Unexpected shell command: " + arguments);
        return new ProcessExecutionResult(0, false, Responses.Dequeue(), "");
    }

    public static string ShellAndGetOutput(string fileName, string arguments = "", int timeout = 1000000)
        => ShellAndGetResult(fileName, arguments, timeout).Output;

    public static string ShellAndGetOutputChecked(string fileName, string arguments = "", int timeout = 1000000,
        string? workingDirectory = null) => ShellAndGetResult(fileName, arguments, timeout, workingDirectory).Output;
}

internal sealed class Process
{
    public static Process[] GetProcessesByName(string name) => [];
    public static Process? Start(ProcessStartInfo info) => throw new InvalidOperationException("Unexpected process launch.");
    public void Kill() => throw new InvalidOperationException("Unexpected process termination.");
    public bool WaitForExit(int milliseconds) => throw new InvalidOperationException("Unexpected process wait.");
}
