using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace PCL;

/// <summary>
/// 在 Windows 创建进程时原子加入 kill-on-close Job，整个命令树共享输出与超时生命周期。
/// 非 Windows 回退只能终止仍存活的根进程及当时可枚举的后代，不能保证回收已脱离根进程的后代。
/// </summary>
internal static class WindowsProcessExecution
{
    internal static ModBase.ProcessExecutionResult Execute(string fileName, string arguments, int timeout, string? directory)
    {
        if (timeout < Timeout.Infinite) throw new ArgumentOutOfRangeException(nameof(timeout));
        return ExecuteAsync().GetAwaiter().GetResult();

        async Task<ModBase.ProcessExecutionResult> ExecuteAsync()
        {
            using var scope = OperatingSystem.IsWindows() ? StartWindows(fileName, arguments, directory) : StartPortable(fileName, arguments, directory);
            using var deadline = new CancellationTokenSource();
            if (timeout != Timeout.Infinite) deadline.CancelAfter(timeout);
            var output = new StringBuilder();
            var error = new StringBuilder();
            var outputTask = CaptureAsync(scope.Output, output, deadline.Token);
            var errorTask = CaptureAsync(scope.Error, error, deadline.Token);
            var timedOut = false;
            try
            {
                await scope.Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await Task.WhenAll(outputTask, errorTask).WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                timedOut = true;
                // Job 仍持有父进程退出后留下的后代，不依赖根进程 HasExited 来决定是否清理。
                scope.TerminateAndWait();
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            return new ModBase.ProcessExecutionResult(scope.Process.ExitCode, timedOut, output.ToString(), error.ToString());
        }
    }

    private static async Task CaptureAsync(StreamReader reader, StringBuilder target, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
                target.Append(buffer, 0, count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private sealed class ProcessScope(Process process, StreamReader output, StreamReader error, KernelHandle? job) : IDisposable
    {
        internal Process Process { get; } = process;
        internal StreamReader Output { get; } = output;
        internal StreamReader Error { get; } = error;

        internal void TerminateAndWait()
        {
            if (job is not null)
            {
                if (!TerminateJobObject(job, 1)) throw NativeError("无法终止超时命令的进程组。");
                var deadline = Environment.TickCount64 + 5000;
                while (true)
                {
                    if (!QueryInformationJobObject(job, 1, out var accounting, Marshal.SizeOf<JobAccounting>(), IntPtr.Zero))
                        throw NativeError("无法确认超时命令的进程组已终止。");
                    if (accounting.ActiveProcesses == 0) break;
                    if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("外部命令超时且未能确认全部子进程已终止。");
                    Thread.Sleep(10);
                }
            }
            else
            {
                try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (Process.HasExited) { }
            }
            if (!Process.WaitForExit(5000)) throw new InvalidOperationException("外部命令超时且未能确认根进程已终止。");
        }

        public void Dispose()
        {
            // 即使主进程正常退出，也不能让仍持有 Job 的后台后代逸出本次命令生命周期。
            job?.Dispose();
            Output.Dispose();
            Error.Dispose();
            Process.Dispose();
        }
    }

    private static ProcessScope StartPortable(string fileName, string arguments, string? directory)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (!string.IsNullOrEmpty(directory)) info.WorkingDirectory = directory;
        var process = Process.Start(info) ?? throw new InvalidOperationException("无法创建外部进程。");
        return new ProcessScope(process, process.StandardOutput, process.StandardError, null);
    }

    private static ProcessScope StartWindows(string fileName, string arguments, string? directory)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('"'))
            throw new ArgumentException("可执行文件名无效。", nameof(fileName));
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw NativeError("无法创建外部命令进程组。"); }
        SafeFileHandle? outputRead = null, errorRead = null;
        StreamReader? output = null, error = null;
        Process? process = null;
        try
        {
            var limits = new JobLimits { Basic = new BasicJobLimits { LimitFlags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>()))
                throw NativeError("无法设置外部命令进程组。");
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
            if (!CreatePipe(out outputRead, out var outputWrite, ref security, 0)) throw NativeError("无法创建命令输出管道。");
            using (outputWrite)
            {
                if (!CreatePipe(out errorRead, out var errorWrite, ref security, 0)) throw NativeError("无法创建命令错误管道。");
                using (errorWrite)
                using (var input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero))
                using (var attributes = new StartupAttributes())
                {
                    if (input.IsInvalid || !SetHandleInformation(outputRead, 1, 0) || !SetHandleInformation(errorRead, 1, 0))
                        throw NativeError("无法设置外部命令管道。");
                    attributes.SetHandles(input, outputWrite, errorWrite, job);
                    var startup = new StartupInfoEx
                    {
                        Info = new StartupInfo
                        {
                            Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                            Input = input.DangerousGetHandle(), Output = outputWrite.DangerousGetHandle(), Error = errorWrite.DangerousGetHandle()
                        },
                        Attributes = attributes.Pointer
                    };
                    var commandLine = new StringBuilder('"' + fileName + '"' + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
                    // JOB_LIST 在 CreateProcess 内原子生效；挂起仅用于先建立托管管道与 Process 包装。
                    if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                            0x4 | 0x80000 | 0x8000000 | 0x400, IntPtr.Zero, string.IsNullOrEmpty(directory) ? null : directory,
                            ref startup, out var created))
                        throw NativeError("无法创建受控外部进程。");
                    using var processHandle = new KernelHandle(created.Process);
                    using var thread = new KernelHandle(created.Thread);
                    // 父进程不得持有写端，否则进程树退出后读取端也永远不会收到 EOF。
                    outputWrite.Dispose();
                    errorWrite.Dispose();
                    process = Process.GetProcessById((int)created.ProcessId);
                    output = new StreamReader(new FileStream(outputRead, FileAccess.Read), Console.OutputEncoding, true);
                    error = new StreamReader(new FileStream(errorRead, FileAccess.Read), Console.OutputEncoding, true);
                    if (ResumeThread(thread) == uint.MaxValue) throw NativeError("无法运行受控外部进程。");
                    return new ProcessScope(process, output, error, job);
                }
            }
        }
        catch
        {
            // 原子 Job 创建成功后，任何后续初始化失败也会结束尚在挂起态的进程树。
            job.Dispose();
            output?.Dispose(); error?.Dispose();
            outputRead?.Dispose(); errorRead?.Dispose();
            process?.Dispose();
            throw;
        }
    }

    private sealed class StartupAttributes : IDisposable
    {
        internal IntPtr Pointer { get; private set; }
        private IntPtr _handles;
        private bool _initialized;
        internal StartupAttributes()
        {
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
            if (size == 0) throw NativeError("无法分配外部进程属性。");
            Pointer = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(Pointer, 2, 0, ref size))
            {
                Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero;
                throw NativeError("无法初始化外部进程属性。");
            }
            _initialized = true;
        }
        internal void SetHandles(SafeFileHandle input, SafeFileHandle output, SafeFileHandle error, KernelHandle job)
        {
            _handles = Marshal.AllocHGlobal(IntPtr.Size * 4);
            Marshal.WriteIntPtr(_handles, 0, input.DangerousGetHandle());
            Marshal.WriteIntPtr(_handles, IntPtr.Size, output.DangerousGetHandle());
            Marshal.WriteIntPtr(_handles, IntPtr.Size * 2, error.DangerousGetHandle());
            Marshal.WriteIntPtr(_handles, IntPtr.Size * 3, job.DangerousGetHandle());
            // 显式继承标准管道，防止并发启动意外继承其他命令的管道/进程组句柄。
            if (!UpdateProcThreadAttribute(Pointer, 0, 0x20002, _handles, (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero) ||
                !UpdateProcThreadAttribute(Pointer, 0, 0x2000D, IntPtr.Add(_handles, IntPtr.Size * 3), (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw NativeError("无法设置受控外部进程属性。");
        }
        public void Dispose()
        {
            if (_initialized) DeleteProcThreadAttributeList(Pointer);
            if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer);
            if (_handles != IntPtr.Zero) Marshal.FreeHGlobal(_handles);
        }
    }

    private static Win32Exception NativeError(string message) => new(Marshal.GetLastWin32Error(), message);
    private sealed class KernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public KernelHandle() : base(true) { }
        internal KernelHandle(IntPtr handle) : base(true) => SetHandle(handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title; public int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        public short ShowWindow, ReservedSize; public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicJobLimits
    {
        public long ProcessTime, JobTime; public uint LimitFlags; public nuint MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits { public BasicJobLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] private struct JobAccounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern KernelHandle CreateJobObjectW(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(KernelHandle job, int infoClass, ref JobLimits limits, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(KernelHandle job, int infoClass, out JobAccounting info, int size, IntPtr returnedLength);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(KernelHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(KernelHandle thread);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
