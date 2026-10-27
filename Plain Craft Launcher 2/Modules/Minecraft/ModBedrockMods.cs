using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PCL;

/// <summary>
///     基岩版 GDK 便携版的 DLL 模组支持。
///     GDK 便携版的主程序是磁盘上的普通文件，不受 UWP 的代码完整性限制，因此可以注入 mods 目录内的 DLL。
///     这里只做最小实现：挂起创建进程 → 恢复主线程让加载器映射 kernel32 → 重新挂起 → CreateRemoteThread(LoadLibraryW) 注入 → 恢复。
/// </summary>
public static class ModBedrockMods
{
    public const string ModsFolderName = "mods";

    /// <summary>与 BedrockBoot 同名的预加载目录：放这里的 DLL 不区分加载器，启动时一律注入。</summary>
    public const string PreloadFolderName = "preload";

    public static string GetModsDirectory(string gameDirectory)
    {
        return Path.Combine(gameDirectory, ModsFolderName);
    }

    public static string GetPreloadDirectory(string gameDirectory)
    {
        return Path.Combine(gameDirectory, PreloadFolderName);
    }

    /// <summary>preload 目录内的 DLL，不依赖加载器选择。</summary>
    public static string[] GetPreloadFiles(string gameDirectory)
    {
        try
        {
            var directory = GetPreloadDirectory(gameDirectory);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static string[] GetModFiles(string gameDirectory)
    {
        try
        {
            var directory = GetModsDirectory(gameDirectory);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>启动游戏，并在需要时注入 DLL 模组；找不到主程序以外的失败都会退化为普通启动。</summary>
    public static Process? Launch(string exePath, string workingDirectory, string? arguments,
        IReadOnlyList<string> mods)
    {
        // 跨架构无法注入：ARM64 版启动器写不进 x64 游戏进程
        if (mods.Count == 0 || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            if (mods.Count > 0)
                ModBase.Log("[Bedrock] 当前启动器不是 x64 架构，已跳过 DLL 模组注入", ModBase.LogLevel.Hint,
                    userSummary: "当前架构不支持基岩版 DLL 模组注入，本次未加载模组");
            return StartDirect(exePath, workingDirectory, arguments);
        }

        try
        {
            return StartWithInjection(exePath, workingDirectory, arguments, mods);
        }
        catch (InjectionProcessTerminationException)
        {
            // 不能确认旧进程结束时禁止再启动一份，更不能继续使用仍在执行注入的进程。
            throw;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, $"注入基岩版 DLL 模组失败（{mods.Count} 个），本次直接启动", ModBase.LogLevel.Hint,
                userSummary: "注入基岩版 DLL 模组失败，本次未加载模组");
            return StartDirect(exePath, workingDirectory, arguments);
        }
    }

    private static Process StartDirect(string exePath, string workingDirectory, string? arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        if (!string.IsNullOrEmpty(arguments))
            startInfo.Arguments = arguments;

        return Process.Start(startInfo)!;
    }

    private static Process? StartWithInjection(string exePath, string workingDirectory, string? arguments,
        IReadOnlyList<string> mods)
    {
        var commandLine = new StringBuilder();
        commandLine.Append('"').Append(exePath).Append('"');
        if (!string.IsNullOrEmpty(arguments))
            commandLine.Append(' ').Append(arguments);

        var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        if (!CreateProcessW(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CreateSuspended | CreateUnicodeEnvironment, IntPtr.Zero, workingDirectory,
                ref startupInfo, out var processInfo))
            throw new InvalidOperationException($"创建游戏进程失败（错误码 {Marshal.GetLastWin32Error()}）");

        var suspended = false;
        try
        {
            // 挂起态下系统加载器还没跑，kernel32 尚未映射，此时无法定位 LoadLibraryW。
            // 先恢复主线程让加载器完成映射，再重新挂起，把注入窗口压到游戏真正初始化之前。
            ResumeThread(processInfo.hThread);
            var remoteLoadLibrary = WaitForRemoteLoadLibrary((int)processInfo.dwProcessId, 5000);
            if (remoteLoadLibrary == IntPtr.Zero)
            {
                ModBase.Log("[Bedrock] 无法定位目标进程中的 LoadLibraryW，跳过 DLL 模组注入", ModBase.LogLevel.Hint,
                    userSummary: "无法注入基岩版 DLL 模组，本次未加载模组");
            }
            else
            {
                SuspendThread(processInfo.hThread);
                suspended = true;
                foreach (var mod in mods)
                    InjectModule(processInfo.hProcess, remoteLoadLibrary, mod);
            }
        }
        catch
        {
            // 注入线程可能仍使用远程参数。结束且确认本次创建的进程退出后，系统会回收其地址空间。
            // 终止失败时禁止普通启动回退，避免遗留游戏进程与下一次启动并存。
            suspended = false;
            AbortCreatedProcess(processInfo.hProcess, WaitForSingleObject,
                handle => TerminateProcess(handle, 1));
            throw;
        }
        finally
        {
            if (suspended) ResumeThread(processInfo.hThread);
            CloseHandle(processInfo.hThread);
            CloseHandle(processInfo.hProcess);
        }

        try
        {
            return Process.GetProcessById((int)processInfo.dwProcessId);
        }
        catch
        {
            return null;
        }
    }

    private static void InjectModule(IntPtr processHandle, IntPtr loadLibrary, string dllPath)
    {
        var fileName = Path.GetFileName(dllPath);
        var bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
        var remotePath = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)bytes.Length, MemCommit | MemReserve,
            PageReadWrite);
        if (remotePath == IntPtr.Zero)
        {
            ModBase.Log($"[Bedrock] 无法在游戏进程中分配内存，跳过模组: {fileName}", ModBase.LogLevel.Hint);
            return;
        }

        var argumentOwnedByThread = false;
        try
        {
            if (!WriteProcessMemory(processHandle, remotePath, bytes, (uint)bytes.Length, out _))
            {
                ModBase.Log($"[Bedrock] 写入模组路径失败，跳过模组: {fileName}", ModBase.LogLevel.Hint);
                return;
            }

            var thread = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibrary, remotePath, 0, out _);
            if (thread == IntPtr.Zero)
            {
                ModBase.Log($"[Bedrock] 创建注入线程失败，跳过模组: {fileName}", ModBase.LogLevel.Hint);
                return;
            }

            argumentOwnedByThread = true;
            var exitCode = CompleteInjectionThread(thread,
                handle => WaitForSingleObject(handle, 15000),
                handle => GetExitCodeThread(handle, out var code)
                    ? code : throw new InvalidOperationException("无法读取 DLL 注入线程的退出状态。"),
                handle => CloseHandle(handle),
                () => VirtualFreeEx(processHandle, remotePath, 0, MemRelease));
            if (exitCode == 0)
                throw new InvalidOperationException($"模组加载失败（DllMain 返回 FALSE 或缺少依赖）: {fileName}");
            ModBase.Log($"[Bedrock] 已注入模组: {fileName}", ModBase.LogLevel.Normal);
        }
        finally
        {
            if (!argumentOwnedByThread) VirtualFreeEx(processHandle, remotePath, 0, MemRelease);
        }
    }

    /// <summary>只在线程确认结束后释放远程参数；超时或等待失败由启动方结束本次创建的进程。</summary>
    internal static uint CompleteInjectionThread(IntPtr thread, Func<IntPtr, uint> wait,
        Func<IntPtr, uint> getExitCode, Action<IntPtr> close, Action releaseArgument)
    {
        var completed = false;
        try
        {
            var waitResult = wait(thread);
            if (waitResult == 0x102) throw new TimeoutException("DLL 模组注入超时，正在中止本次游戏启动。");
            if (waitResult != WaitObject0) throw new InvalidOperationException("无法确认 DLL 注入线程已经结束。");
            completed = true;
            return getExitCode(thread);
        }
        finally
        {
            try { if (completed) releaseArgument(); }
            finally { close(thread); }
        }
    }

    private sealed class InjectionProcessTerminationException()
        : InvalidOperationException("DLL 模组注入失败，且无法确认本次创建的游戏进程已终止；已中止启动。");

    internal static void AbortCreatedProcess(IntPtr process, Func<IntPtr, uint, uint> wait, Action<IntPtr> terminate)
    {
        if (wait(process, 0) == WaitObject0) return;
        terminate(process);
        // TerminateProcess 可能与自然退出竞争，以最终等待结果为准。
        if (wait(process, 5000) != WaitObject0) throw new InjectionProcessTerminationException();
    }

    /// <summary>等待目标进程完成加载器初始化，再按模块基址换算出远程 LoadLibraryW 地址。</summary>
    private static IntPtr WaitForRemoteLoadLibrary(int processId, int timeoutMs)
    {
        var localKernel32 = GetModuleHandleW("kernel32.dll");
        var localLoadLibrary = GetProcAddress(localKernel32, "LoadLibraryW");
        if (localKernel32 == IntPtr.Zero || localLoadLibrary == IntPtr.Zero) return IntPtr.Zero;

        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var remoteKernel32 = GetRemoteModuleBase(processId, "kernel32.dll");
            if (remoteKernel32 != IntPtr.Zero)
                return (IntPtr)((long)remoteKernel32 + ((long)localLoadLibrary - (long)localKernel32));
            if (Environment.TickCount64 >= deadline) return IntPtr.Zero;
            Thread.Sleep(1);
        }
    }

    private static IntPtr GetRemoteModuleBase(int processId, string moduleName)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapModule | SnapModule32, (uint)processId);
        if (snapshot == InvalidHandleValue) return IntPtr.Zero;

        try
        {
            var entry = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
            if (!Module32FirstW(snapshot, ref entry)) return IntPtr.Zero;

            do
            {
                if (string.Equals(entry.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                    return entry.modBaseAddr;
            } while (Module32NextW(snapshot, ref entry));

            return IntPtr.Zero;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    #region 原生互操作

    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint MemCommit = 0x00001000;
    private const uint MemReserve = 0x00002000;
    private const uint MemRelease = 0x00008000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0;
    private const uint SnapModule = 0x00000008;
    private const uint SnapModule32 = 0x00000010;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize,
        out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

    #endregion
}
