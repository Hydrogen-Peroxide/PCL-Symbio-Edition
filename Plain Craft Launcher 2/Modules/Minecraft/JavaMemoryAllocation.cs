using System.IO;
using PCL.Core.App;
using PCL.Core.Utils.OS;

namespace PCL;

internal static class JavaMemoryAllocation
{
    public static double GetRamFromTick(int value) => value switch
    {
        <= 12 => value * .1 + .3,
        <= 25 => (value - 12) * .5 + 1.5,
        <= 33 => value - 25 + 8,
        _ => (value - 33) * 2 + 16
    };

    public static bool HasCustomXms(McInstance? version)
    {
        var instanceArguments = version is null ? "" : Config.Instance.JvmArgs[version.PathInstance];
        var arguments = string.IsNullOrEmpty(instanceArguments) ? Config.Launch.JvmArgs : instanceArguments;
        return arguments?.Contains("-Xms", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static double? GetInitialRam(McInstance? version, bool useVersionJavaSetup, bool? is32BitJava = null)
    {
        if (Config.Launch.LockMemory || HasCustomXms(version) || Config.Launch.MemoryAllocationMode != 1) return null;
        var initial = Config.Launch.CustomInitialMemorySize;
        return initial <= 0 ? null : Math.Min(GetRamFromTick(initial), GetRam(version, useVersionJavaSetup, is32BitJava));
    }

    public static double GetRam(McInstance? version, bool useVersionJavaSetup, bool? is32BitJava = null)
    {
        var allocated = Config.Launch.MemoryAllocationMode == 0
            ? AutomaticRam(version, Math.Round((double)KernelInterop.GetAvailablePhysicalMemoryBytes() / 1024 / 1024 / 1024 * 10) / 10)
            : GetRamFromTick(Config.Launch.CustomMemorySize);
        return ApplyArchitectureLimit(allocated, useVersionJavaSetup ? version : null, is32BitJava);
    }

    public static double GetInstanceRam(McInstance? version, bool? is32BitJava = null)
    {
        if (version is null || Config.Instance.MemorySolution[version.PathInstance] == 2)
            return GetRam(version, true, is32BitJava);
        var path = version.PathInstance;
        var allocated = Config.Instance.MemorySolution[path] == 0
            ? AutomaticRam(version, KernelInterop.GetAvailablePhysicalMemoryBytes() / 1024 / 1024 / 1024)
            : GetRamFromTick(Config.Instance.CustomMemorySize[path]);
        return ApplyArchitectureLimit(allocated, version, is32BitJava);
    }

    public static double? GetInstanceInitialRam(McInstance? version, bool? is32BitJava = null)
    {
        if (Config.Launch.LockMemory || HasCustomXms(version)) return null;
        if (version is null || Config.Instance.MemorySolution[version.PathInstance] == 2)
            return GetInitialRam(version, true, is32BitJava);
        if (Config.Instance.MemorySolution[version.PathInstance] != 1) return null;
        var initial = Config.Instance.CustomInitialMemorySize[version.PathInstance];
        return initial <= 0 ? null : Math.Min(GetRamFromTick(initial), GetInstanceRam(version, is32BitJava));
    }

    private static double ApplyArchitectureLimit(double allocated, McInstance? version, bool? is32BitJava) =>
        (is32BitJava ?? !ModJava.IsGameSet64BitJava(version)) ? Math.Min(1, allocated) : allocated;

    private static double AutomaticRam(McInstance? version, double available)
    {
        if (version is not null && !version.IsLoaded) version.Load();
        double minimum = .5, low = 1.5, medium = 2.5, high = 4;
        if (version?.Modable == true)
        {
            var mods = new DirectoryInfo(Path.Combine(version.PathIndie, "mods"));
            var count = mods.Exists ? mods.GetFiles().Length : 0;
            minimum += count / 150d;
            low += count / 90d;
            medium = 2.7 + count / 50d;
            high = 4.5 + count / 25d;
        }
        else if (version?.Info.HasOptiFine == true)
        {
            medium = 3;
            high = 5;
        }

        double allocated = 0;
        foreach (var (limit, weight) in new[] { (low, 1d), (medium - low, .7), (high - medium, .4), (high, .15) })
        {
            allocated += Math.Min(available * weight, limit);
            available -= limit / weight;
            if (available < .1) break;
        }
        return Math.Round(Math.Max(allocated, minimum), 1);
    }
}
