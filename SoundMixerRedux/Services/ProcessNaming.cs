using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoundMixerRedux.Services;

/// <summary>
/// Resolves a friendly app name (and the process to take the icon from) for an audio session.
/// Walks up out of WebView2 host processes (new Teams, etc.) to the owning application, the way
/// the Windows Volume Mixer resolves app identity rather than showing the raw host process.
/// </summary>
public static class ProcessNaming
{
    private const int MaxHops = 4;

    /// <returns>(friendly name, pid to extract the icon from).</returns>
    public static (string Name, uint IconPid) Resolve(uint pid, bool isSystemSounds)
    {
        if (isSystemSounds) return ("Sons système", 0);
        if (pid == 0) return ("Application", 0);

        uint current = pid;
        for (int hop = 0; hop < MaxHops; hop++)
        {
            var (name, processName) = Friendly(current);
            bool isWebViewHost = processName != null &&
                processName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase);

            if (!isWebViewHost && name != null)
                return (name, current);

            uint parent = GetParentPid(current);
            if (parent == 0 || parent == current) break;
            current = parent;
        }

        var (fallback, _) = Friendly(pid);
        return (fallback ?? $"App {pid}", pid);
    }

    private static (string? Name, string? ProcessName) Friendly(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            string procName = p.ProcessName;
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) return (desc, procName);
            }
            catch { /* MainModule inaccessible (access denied / cross-arch) */ }
            return (procName, procName);
        }
        catch { return (null, null); }
    }

    // ---- Parent PID lookup via Toolhelp snapshot ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static uint GetParentPid(uint pid)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == InvalidHandle) return 0;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (entry.th32ProcessID == pid)
                        return entry.th32ParentProcessID;
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally { CloseHandle(snapshot); }
        return 0;
    }
}
