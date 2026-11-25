namespace PCL;

internal static class JavaInstallRequests
{
    private static string? pendingVersion;
    public static string? PendingVersion => Volatile.Read(ref pendingVersion);
    public static event Action? VersionRequested;

    public static void RequestVersion(string version)
    {
        Interlocked.Exchange(ref pendingVersion, version);
        ModBase.RunInUi(() => VersionRequested?.Invoke());
    }

    public static void AcceptVersion(string version) => Interlocked.CompareExchange(ref pendingVersion, null, version);
}
