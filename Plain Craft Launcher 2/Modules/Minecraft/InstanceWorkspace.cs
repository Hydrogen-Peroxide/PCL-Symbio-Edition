namespace PCL;

/// <summary>Selection for the instance editor, which may differ from the instance selected for launching.</summary>
internal static class InstanceWorkspace
{
    internal static McInstance? Current { get; set; }
    internal static string? SaveDirectory { get; set; }
}
