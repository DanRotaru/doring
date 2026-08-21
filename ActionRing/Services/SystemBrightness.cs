using System.Diagnostics;
using System.Globalization;
using ActionRing.Interop;

namespace ActionRing.Services;

/// <summary>Reads and changes brightness through DDC/CI, with WMI fallback for laptop panels.</summary>
internal static class SystemBrightness
{
    private static int? _lastPercent;

    public static int GetPercent()
    {
        if (TryReadPhysical(out var value)) return value;
        if (_lastPercent is not null) return _lastPercent.Value;

        var output = RunPowerShell(
            "@(Get-CimInstance -Namespace root/WMI -Class WmiMonitorBrightness)[0].CurrentBrightness");
        if (int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            _lastPercent = Math.Clamp(value, 0, 100);
        return _lastPercent ?? 0;
    }

    public static int Change(int percentagePoints)
    {
        if (TryChangePhysical(percentagePoints, out var value))
        {
            _lastPercent = value;
            return value;
        }

        var direction = percentagePoints >= 0 ? $"+ {percentagePoints}" : $"- {Math.Abs(percentagePoints)}";
        var output = RunPowerShell(
            "$b=@(Get-CimInstance -Namespace root/WMI -Class WmiMonitorBrightness)[0].CurrentBrightness;" +
            "$n=[Math]::Max(0,[Math]::Min(100,$b " + direction + "));" +
            "@(Get-CimInstance -Namespace root/WMI -Class WmiMonitorBrightnessMethods)|ForEach-Object {$_.WmiSetBrightness(1,$n)|Out-Null};$n");
        if (int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            _lastPercent = Math.Clamp(value, 0, 100);
        return _lastPercent ?? GetPercent();
    }

    private static bool TryReadPhysical(out int percentage)
    {
        var values = new List<int>();
        VisitPhysicalMonitors((monitor, min, current, max) =>
        {
            values.Add((int)Math.Round((current - min) * 100d / (max - min)));
        });
        percentage = values.Count == 0 ? 0 : Math.Clamp((int)Math.Round(values.Average()), 0, 100);
        return values.Count > 0;
    }

    private static bool TryChangePhysical(int delta, out int percentage)
    {
        var values = new List<int>();
        VisitPhysicalMonitors((monitor, min, current, max) =>
        {
            var currentPercent = (current - min) * 100d / (max - min);
            var nextPercent = Math.Clamp(currentPercent + delta, 0, 100);
            var requested = min + (uint)Math.Round((max - min) * nextPercent / 100d);
            if (NativeMethods.SetMonitorBrightness(monitor, requested)) values.Add((int)Math.Round(nextPercent));
        });
        percentage = values.Count == 0 ? 0 : Math.Clamp((int)Math.Round(values.Average()), 0, 100);
        return values.Count > 0;
    }

    private static void VisitPhysicalMonitors(Action<IntPtr, uint, uint, uint> visitor)
    {
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr logicalMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out var count) || count == 0)
                    return true;
                var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, monitors)) return true;
                try
                {
                    foreach (var physical in monitors)
                        if (NativeMethods.GetMonitorBrightness(physical.Handle, out var min, out var current, out var max) && max > min)
                            visitor(physical.Handle, min, current, max);
                }
                finally
                {
                    NativeMethods.DestroyPhysicalMonitors(count, monitors);
                }
                return true;
            }, IntPtr.Zero);
    }

    private static string RunPowerShell(string script)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-WindowStyle");
            start.ArgumentList.Add("Hidden");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(script);
            using var process = Process.Start(start);
            if (process is null) return "";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
