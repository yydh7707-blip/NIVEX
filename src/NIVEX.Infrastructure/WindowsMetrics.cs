using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NIVEX.Application;
using NIVEX.Domain;

namespace NIVEX.Infrastructure;

public sealed class WindowsMetrics : ISystemMetrics, IDisposable
{
    private ulong _idle, _kernel, _user;
    private bool _hasCpu;
    private bool _gpuFailed;
    private string? _gpuName;
    private PerformanceCounter[]? _gpuCounters;

    public SystemSnapshot Read()
    {
        var cpu = ReadCpu(out var pending);
        var (used, total) = ReadMemory();
        var (gpuName, gpuPercent, gpuAvailable) = ReadGpu();
        var (diskUsed, diskTotal) = ReadDisk();
        return new SystemSnapshot(cpu, pending, used, total, gpuName, gpuPercent, gpuAvailable, diskUsed, diskTotal, ReadNetwork());
    }

    private double? ReadCpu(out bool pending)
    {
        pending = false;
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        if (!_hasCpu)
        {
            _idle = idle.Value; _kernel = kernel.Value; _user = user.Value; _hasCpu = true;
            pending = true;
            return null;
        }
        var percent = CpuMath.Percent(_idle, _kernel, _user, idle.Value, kernel.Value, user.Value);
        _idle = idle.Value; _kernel = kernel.Value; _user = user.Value;
        return percent;
    }

    private static (ulong? Used, ulong? Total) ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (null, null);
        return (status.TotalPhys - status.AvailPhys, status.TotalPhys);
    }

    private (string? Name, double? Percent, bool Available) ReadGpu()
    {
        _gpuName ??= TryGpuName();
        if (_gpuFailed) return (_gpuName, null, false);
        try
        {
            if (_gpuCounters is null)
            {
                var created = new List<PerformanceCounter>();
                var category = new PerformanceCounterCategory("GPU Engine");
                foreach (var instance in category.GetInstanceNames())
                {
                    if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    counter.NextValue();
                    created.Add(counter);
                }
                _gpuCounters = created.ToArray();
                return (_gpuName, null, _gpuCounters.Length > 0);
            }
            if (_gpuCounters.Length == 0) return (_gpuName, null, false);
            double sum = 0;
            foreach (var counter in _gpuCounters) sum += counter.NextValue();
            return (_gpuName, Math.Clamp(sum, 0, 100), true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _gpuFailed = true;
            return (_gpuName, null, false);
        }
    }

    public void Dispose()
    {
        if (_gpuCounters is null) return;
        foreach (var counter in _gpuCounters) counter.Dispose();
        _gpuCounters = null;
    }

    private static string? TryGpuName()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception exception) when (exception is System.Management.ManagementException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }
        return null;
    }

    private static (ulong? Used, ulong? Total) ReadDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (root is null) return (null, null);
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return (null, null);
            return ((ulong)(drive.TotalSize - drive.AvailableFreeSpace), (ulong)drive.TotalSize);
        }
        catch (IOException)
        {
            return (null, null);
        }
    }

    private static bool? ReadNetwork()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(item =>
                item.OperationalStatus == OperationalStatus.Up
                && item.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
