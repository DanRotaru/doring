using ActionRing.Interop;

namespace ActionRing.Services;

/// <summary>
/// Hands idle memory back to the OS.
///
/// This app spends effectively all of its life waiting for a hotkey, so the
/// pages it touched while the ring was on screen are dead weight afterwards.
/// A collection followed by EmptyWorkingSet moves them out of the process's
/// resident set; Windows re-faults whatever is needed on the next summon, which
/// costs a few milliseconds nobody can perceive against a hotkey press.
///
/// Note this trims the *working set*, not what the process has committed.
/// It is the number Task Manager shows, and the physical RAM really is
/// returned, but the reservation is still there.
/// </summary>
public static class MemoryTrim
{
    public static void Now()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive,
            blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
    }
}
