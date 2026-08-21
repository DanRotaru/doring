using System.Runtime.InteropServices;

namespace ActionRing.Services;

/// <summary>Reads and changes the Windows Core Audio master volume.</summary>
internal static class SystemVolume
{
    private const uint ClsCtxAll = 23;
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static int GetPercent()
    {
        try
        {
            using var endpoint = Open();
            endpoint.Volume.GetMasterVolumeLevelScalar(out var scalar);
            return Math.Clamp((int)Math.Round(scalar * 100), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public static int Change(int percentagePoints)
    {
        try
        {
            using var endpoint = Open();
            endpoint.Volume.GetMasterVolumeLevelScalar(out var scalar);
            var next = Math.Clamp(scalar + percentagePoints / 100f, 0f, 1f);
            var eventContext = Guid.Empty;
            endpoint.Volume.SetMasterVolumeLevelScalar(next, ref eventContext);
            return Math.Clamp((int)Math.Round(next * 100), 0, 100);
        }
        catch
        {
            return GetPercent();
        }
    }

    private static AudioEndpoint Open()
    {
        var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
        var iid = EndpointVolumeId;
        device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var volumeObject);
        return new AudioEndpoint(enumerator, device, (IAudioEndpointVolume)volumeObject);
    }

    private sealed class AudioEndpoint : IDisposable
    {
        private readonly object _enumerator;
        private readonly object _device;
        public IAudioEndpointVolume Volume { get; }

        public AudioEndpoint(object enumerator, object device, IAudioEndpointVolume volume)
        {
            _enumerator = enumerator;
            _device = device;
            Volume = volume;
        }

        public void Dispose()
        {
            Marshal.FinalReleaseComObject(Volume);
            Marshal.FinalReleaseComObject(_device);
            Marshal.FinalReleaseComObject(_enumerator);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute(out bool mute);
    }
}
