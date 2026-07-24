using System;
using System.Runtime.InteropServices;

namespace SoundMixerRedux.Services;

/// <summary>
/// Switches the Windows default audio endpoint via the (undocumented but stable since Win7)
/// IPolicyConfig COM interface — the same mechanism EarTrumpet / SoundSwitch use, since Windows
/// exposes no public API for this.
/// </summary>
internal static class PolicyConfig
{
    /// <summary>Make <paramref name="deviceId"/> the default for all roles (Console, Multimedia, Communications).</summary>
    public static void SetDefault(string deviceId)
    {
        var config = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            config.SetDefaultEndpoint(deviceId, ERole.Console);
            config.SetDefaultEndpoint(deviceId, ERole.Multimedia);
            config.SetDefaultEndpoint(deviceId, ERole.Communications);
        }
        finally
        {
            Marshal.ReleaseComObject(config);
        }
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    // Only SetDefaultEndpoint is called; the earlier members are declared purely to keep the
    // vtable slots aligned (their signatures are stubs and never invoked).
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }
}
