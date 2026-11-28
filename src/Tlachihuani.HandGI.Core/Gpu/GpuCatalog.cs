// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Runtime.InteropServices;
using System.Text;

namespace HandGI.Core.Gpu;

/// <summary>One display adapter as seen by DXGI (the index DirectML uses), enriched with NVML data for NVIDIA GPUs.</summary>
public sealed record GpuInfo(
    int DxgiIndex,
    string Name,
    uint VendorId,
    uint DeviceId,
    ulong DedicatedVideoMemory,
    bool IsSoftware,
    string? DriverVersion,
    int? ComputeMajor,
    int? ComputeMinor)
{
    public const uint NvidiaVendorId = 0x10DE;

    public bool IsNvidia => VendorId == NvidiaVendorId;

    public string ComputeCapability => ComputeMajor is int ma && ComputeMinor is int mi ? $"{ma}.{mi}" : "unknown";

    public string Architecture => ComputeMajor switch
    {
        6 => "Pascal",
        7 when ComputeMinor == 5 => "Turing",
        7 => "Volta",
        8 when ComputeMinor == 9 => "Ada Lovelace",
        8 => "Ampere",
        9 => "Hopper",
        >= 10 and <= 12 => "Blackwell",
        > 12 => "newer than Blackwell",
        null => "unknown",
        _ => "older than Pascal",
    };

    public string VramText => $"{DedicatedVideoMemory / (1024.0 * 1024 * 1024):0.#} GB";

    public string Summary => $"{Name} · {VramText} · CC {ComputeCapability} ({Architecture})" + (DriverVersion is null ? "" : $" · driver {DriverVersion}");

    /// <summary>
    /// HandGI's support rule: an NVIDIA GPU from the GTX 10 series (Pascal, compute 6.x) up to the
    /// RTX 50 series (Blackwell) and anything newer. Older or non-NVIDIA adapters are refused.
    /// </summary>
    public (bool Supported, string Reason) CheckSupport()
    {
        if (IsSoftware) return (false, "software adapter (WARP)");
        if (!IsNvidia) return (false, "not an NVIDIA GPU");
        if (ComputeMajor is int major)
        {
            if (major < 6) return (false, $"compute capability {ComputeCapability} is older than Pascal (GTX 10 series)");
            return (true, $"{Architecture}, compute capability {ComputeCapability}");
        }
        // NVML unavailable: accept the NVIDIA adapter but say we could not verify the generation.
        return (true, "NVIDIA GPU (generation not verified: NVML unavailable)");
    }

    public override string ToString() => $"[{DxgiIndex}] {Name}";
}

public static class GpuCatalog
{
    /// <summary>Enumerates every DXGI adapter in the order DirectML expects for its device index.</summary>
    public static IReadOnlyList<GpuInfo> Enumerate()
    {
        var result = new List<GpuInfo>();
        var nvidia = NvmlProbe.Query();
        var guid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref guid, out var factory) != 0 || factory is null) return result;
        try
        {
            for (uint i = 0; ; i++)
            {
                if (factory.EnumAdapters1(i, out var adapter) != 0 || adapter is null) break;
                try
                {
                    if (adapter.GetDesc1(out var desc) != 0) continue;
                    bool software = (desc.Flags & 2) != 0;
                    var nv = desc.VendorId == GpuInfo.NvidiaVendorId
                        ? nvidia.FirstOrDefault(n => n.PciDeviceId == desc.DeviceId && !n.Claimed)
                        : null;
                    if (nv is not null) nv.Claimed = true;
                    result.Add(new GpuInfo(
                        (int)i,
                        desc.Description?.TrimEnd('\0') ?? "Unknown adapter",
                        desc.VendorId,
                        desc.DeviceId,
                        (ulong)desc.DedicatedVideoMemory,
                        software,
                        nv?.DriverVersion,
                        nv?.Major,
                        nv?.Minor));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return result;
    }

    /// <summary>The best supported NVIDIA GPU (most video memory), or null.</summary>
    public static GpuInfo? PickBest(IReadOnlyList<GpuInfo> gpus) =>
        gpus.Where(g => g.CheckSupport().Supported).OrderByDescending(g => g.DedicatedVideoMemory).FirstOrDefault();

    // ---------------------------------------------------------------- DXGI interop

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1? factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIFactory
        [PreserveSig] int EnumAdapters(uint index, out IntPtr adapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr window, uint flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr window);
        [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // IDXGIObject
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
        // IDXGIAdapter
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
        [PreserveSig] int GetDesc(IntPtr desc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long umdVersion);
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}

/// <summary>Reads NVIDIA-specific facts (compute capability, driver, live utilisation) through nvml.dll, which ships with the driver.</summary>
public static class NvmlProbe
{
    public sealed class NvidiaDevice
    {
        public uint Index;
        public string Name = "";
        public uint PciDeviceId;
        public int? Major;
        public int? Minor;
        public string? DriverVersion;
        public IntPtr Handle;
        internal bool Claimed;
    }

    private static readonly object Gate = new();
    private static bool _initialized;
    private static bool _available;

    public static bool Available
    {
        get
        {
            EnsureInit();
            return _available;
        }
    }

    private static void EnsureInit()
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                _available = nvmlInit_v2() == 0;
            }
            catch
            {
                _available = false;
            }
        }
    }

    public static List<NvidiaDevice> Query()
    {
        var list = new List<NvidiaDevice>();
        EnsureInit();
        if (!_available) return list;
        try
        {
            string? driver = null;
            var buf = new byte[96];
            if (nvmlSystemGetDriverVersion(buf, (uint)buf.Length) == 0) driver = CString(buf);
            if (nvmlDeviceGetCount_v2(out uint count) != 0) return list;
            for (uint i = 0; i < count; i++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(i, out var h) != 0) continue;
                var d = new NvidiaDevice { Index = i, Handle = h, DriverVersion = driver };
                var nameBuf = new byte[128];
                if (nvmlDeviceGetName(h, nameBuf, (uint)nameBuf.Length) == 0) d.Name = CString(nameBuf);
                if (nvmlDeviceGetCudaComputeCapability(h, out int major, out int minor) == 0)
                {
                    d.Major = major;
                    d.Minor = minor;
                }
                if (TryPci(h, out var pci)) d.PciDeviceId = pci.PciDeviceId >> 16;
                list.Add(d);
            }
        }
        catch
        {
            // NVML is optional
        }
        return list;
    }

    /// <summary>GPU core utilisation in percent for the NVML device matching a DXGI device id, or null.</summary>
    public static (uint GpuPercent, uint MemoryPercent, uint? TemperatureC)? Utilization(uint pciDeviceId)
    {
        EnsureInit();
        if (!_available) return null;
        try
        {
            if (nvmlDeviceGetCount_v2(out uint count) != 0) return null;
            for (uint i = 0; i < count; i++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(i, out var h) != 0) continue;
                if (!TryPci(h, out var pci) || (pci.PciDeviceId >> 16) != pciDeviceId) continue;
                if (nvmlDeviceGetUtilizationRates(h, out var u) != 0) return null;
                uint? temp = nvmlDeviceGetTemperature(h, 0, out uint t) == 0 ? t : null;
                return (u.Gpu, u.Memory, temp);
            }
        }
        catch
        {
            // optional telemetry
        }
        return null;
    }

    private static bool TryPci(IntPtr h, out NvmlPciInfo pci)
    {
        try
        {
            if (nvmlDeviceGetPciInfo_v3(h, out pci) == 0) return true;
        }
        catch (EntryPointNotFoundException)
        {
            // older drivers
        }
        try
        {
            return nvmlDeviceGetPciInfo_v2(h, out pci) == 0;
        }
        catch
        {
            pci = default;
            return false;
        }
    }

    private static string CString(byte[] buffer)
    {
        int n = Array.IndexOf(buffer, (byte)0);
        return Encoding.ASCII.GetString(buffer, 0, n < 0 ? buffer.Length : n);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct NvmlPciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string BusIdLegacy;
        public uint Domain;
        public uint Bus;
        public uint Device;
        public uint PciDeviceId;
        public uint PciSubSystemId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string BusId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlSystemGetDriverVersion(byte[] version, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCudaComputeCapability(IntPtr device, out int major, out int minor);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPciInfo_v3(IntPtr device, out NvmlPciInfo pci);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPciInfo_v2(IntPtr device, out NvmlPciInfo pci);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);
}
