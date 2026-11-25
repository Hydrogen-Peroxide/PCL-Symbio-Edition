using PCLLF.Wpf.Motion;

namespace PCL;

public static partial class ModAnimation
{
    public static double aniSpeed
    {
        get => MotionPreferences.PlaybackRate;
        set => MotionPreferences.PlaybackRate = value;
    }

    private static int controlSuppression;

    public static void AniBeginControl() => Interlocked.Increment(ref controlSuppression);
    public static void AniEndControl() => Interlocked.Decrement(ref controlSuppression);

    public static bool AniExitsAnyControl() => Volatile.Read(ref controlSuppression) != 0;

    public static int AniResetControl(int value) => Interlocked.Exchange(ref controlSuppression, value);
    public static int AniRestoreControl(int value) => Interlocked.Add(ref controlSuppression, value);
}
