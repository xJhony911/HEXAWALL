using System.Diagnostics;
using System.Runtime.InteropServices;
namespace HexaWall;
internal static class MemorySnapshot
{
    internal sealed record Sample(int Id, double WorkingSetMiB, double PrivateCommitMiB, double? PrivateWorkingSetMiB);
    [StructLayout(LayoutKind.Sequential)]
    private struct Counters
    {
        public uint Size, Faults;
        public nuint PeakWorkingSet, WorkingSet, PeakPaged, Paged, PeakNonPaged, NonPaged;
        public nuint PageFile, PeakPageFile, PrivateUsage, PrivateWorkingSet;
        public ulong SharedCommit;
    }
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref Counters counters, uint size);
    public static Sample Read()
    {
        using var process = Process.GetCurrentProcess();
        var data = new Counters { Size = (uint)Marshal.SizeOf<Counters>() };
        bool supported = GetProcessMemoryInfo(process.Handle, ref data, data.Size);
        return new(process.Id, process.WorkingSet64 / 1048576d,
            process.PrivateMemorySize64 / 1048576d,
            supported ? (double?)((ulong)data.PrivateWorkingSet / 1048576d) : null);
    }
}
