Run with the .NET 10 SDK on Windows:

```powershell
dotnet run --project tests/LauncherServicesRegression/LauncherServicesRegression.csproj
```

The host links the production configuration, GitHub update, Windows process helper and injection lifetime code. It extracts the marked process methods directly from ModBase before compiling. WPF, HTTP and UI plumbing are stubbed; release responses are fixtures. Process tests only create this host's own child processes, including a parent that exits while its child holds the output pipe. Injection tests use mocked native wait/handle/memory operations and do not inject DLLs or launch a game. No installed package, account, save or system setting is accessed.

Configuration failures use an in-memory provider that throws. Notification callbacks are controlled fixtures that block or throw; Stop must complete without their response. GitHub fixtures interleave two version queries across refreshes and verify each caller's immutable version/hash/URL snapshot.

On Windows 10+, `PROC_THREAD_ATTRIBUTE_JOB_LIST` binds the process to a kill-on-close Job during creation, before the suspended primary thread is resumed. An explicit inherited handle list prevents unrelated handles from entering the child. Timeout terminates the Job and confirms zero active processes. This covers processes created in that Job; an independent process launched by an external service is outside its scope. The non-Windows fallback only terminates a root that is still alive and its then-enumerable descendants, so it does not promise equivalent cleanup after the root exits.

Win32 references: [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects), [process creation attributes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute), [TerminateJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-terminatejobobject).
