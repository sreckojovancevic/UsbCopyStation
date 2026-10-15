using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UsbCopyStation.Native;

namespace UsbCopyStation.Core;

internal sealed class DiskInfo
{
    public int DiskNumber { get; init; }
    public string DevicePath { get; init; } = "";
    public string InstanceId { get; init; } = "";
    /// <summary>Stabilna putanja fizičkog porta, npr. PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)#USB(2).</summary>
    public string LocationPath { get; init; } = "";
    /// <summary>Čitljiv opis porta, npr. Port_#0002.Hub_#0004.</summary>
    public string LocationInfo { get; init; } = "";
    public int PortDevInst { get; init; }
    public string Vendor { get; init; } = "";
    public string Product { get; init; } = "";
    public string Serial { get; init; } = "";
    public int BusType { get; init; }
    public long Size { get; init; }
    public int SectorSize { get; init; } = 512;
    public List<string> VolumeMountPoints { get; } = new();
    public int VolumeCount { get; set; }

    public bool IsUsb => BusType == Win32.STORAGE_BUS_TYPE_USB;
    public bool HasVolumes => VolumeCount > 0;
    public string Model => $"{Vendor} {Product}".Trim() is { Length: > 0 } m ? m : "USB disk";
    /// <summary>Ključ slota: fizički port ako je poznat, inače instanca uređaja.</summary>
    public string SlotKey => LocationPath.Length > 0 ? LocationPath : InstanceId;
}

internal static class DiskEnumerator
{
    /// <summary>Svi prisutni diskovi sa medijom; filtriranje (USB, izuzeci) radi pozivalac.</summary>
    public static List<DiskInfo> EnumerateDisks()
    {
        var disks = new List<DiskInfo>();
        var guid = Win32.GUID_DEVINTERFACE_DISK;
        IntPtr set = Win32.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
            Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return disks;

        try
        {
            for (int index = 0; ; index++)
            {
                var ifData = new Win32.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Win32.SP_DEVICE_INTERFACE_DATA>() };
                if (!Win32.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref ifData)) break;

                var devInfo = new Win32.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<Win32.SP_DEVINFO_DATA>() };
                Win32.SetupDiGetDeviceInterfaceDetailW(set, ref ifData, IntPtr.Zero, 0, out int required, ref devInfo);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                string devicePath;
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!Win32.SetupDiGetDeviceInterfaceDetailW(set, ref ifData, detail, required, out _, ref devInfo)) continue;
                    devicePath = Marshal.PtrToStringUni(detail + 4) ?? "";
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }

                var disk = TryReadDisk(devicePath, devInfo.DevInst);
                if (disk is not null) disks.Add(disk);
            }
        }
        finally
        {
            Win32.SetupDiDestroyDeviceInfoList(set);
        }

        // volumeni po disku (za prikaz i proveru "ima podatke")
        var volumes = VolumeUtil.EnumerateVolumes();
        foreach (var d in disks)
        {
            foreach (var v in volumes.Where(v => v.Disks.Contains(d.DiskNumber)))
            {
                d.VolumeCount++;
                d.VolumeMountPoints.AddRange(v.MountPoints);
            }
        }
        return disks;
    }

    private static DiskInfo? TryReadDisk(string devicePath, int devInst)
    {
        using var h = Win32.TryOpen(devicePath, Win32.GENERIC_READ);
        if (h is null) return null;

        var num = Win32.Ioctl(h, Win32.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 12);
        if (num is null) return null;
        int diskNumber = BitConverter.ToInt32(num, 4);

        var len = Win32.Ioctl(h, Win32.IOCTL_DISK_GET_LENGTH_INFO, null, 8);
        if (len is null) return null; // npr. čitač kartica bez kartice
        long size = BitConverter.ToInt64(len, 0);
        if (size <= 0) return null;

        int sector = 512;
        var geo = Win32.Ioctl(h, Win32.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 256);
        if (geo is not null)
        {
            int bps = BitConverter.ToInt32(geo, 20);
            if (bps >= 512 && bps <= 65536 && (bps & (bps - 1)) == 0) sector = bps;
        }

        // STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery }
        var desc = Win32.Ioctl(h, Win32.IOCTL_STORAGE_QUERY_PROPERTY, new byte[12], 1024);
        string vendor = "", product = "", serial = "";
        int bus = 0;
        if (desc is not null && desc.Length >= 32)
        {
            vendor = AsciiAt(desc, BitConverter.ToInt32(desc, 12));
            product = AsciiAt(desc, BitConverter.ToInt32(desc, 16));
            serial = AsciiAt(desc, BitConverter.ToInt32(desc, 24));
            bus = BitConverter.ToInt32(desc, 28);
        }

        var (locPath, locInfo, portInst) = FindLocation(devInst);

        return new DiskInfo
        {
            DiskNumber = diskNumber,
            DevicePath = devicePath,
            InstanceId = DeviceId(devInst),
            LocationPath = locPath,
            LocationInfo = locInfo,
            PortDevInst = portInst,
            Vendor = vendor,
            Product = product,
            Serial = serial,
            BusType = bus,
            Size = size,
            SectorSize = sector,
        };
    }

    public static int? GetDiskNumber(SafeFileHandle h)
    {
        var num = Win32.Ioctl(h, Win32.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 12);
        return num is null ? null : BitConverter.ToInt32(num, 4);
    }

    /// <summary>Penje se uz stablo uređaja do čvora koji ima LocationPaths (USB uređaj na portu).</summary>
    private static (string path, string info, int devInst) FindLocation(int devInst)
    {
        int current = devInst;
        for (int depth = 0; depth < 8; depth++)
        {
            var paths = GetStringList(current, Win32.DEVPKEY_Device_LocationPaths);
            if (paths.Count > 0)
            {
                var info = GetStringList(current, Win32.DEVPKEY_Device_LocationInfo);
                return (paths[0], info.Count > 0 ? info[0] : "", current);
            }
            if (Win32.CM_Get_Parent(out int parent, current, 0) != Win32.CR_SUCCESS) break;
            current = parent;
        }
        return ("", "", devInst);
    }

    private static List<string> GetStringList(int devInst, Win32.DEVPROPKEY key)
    {
        int size = 0;
        int cr = Win32.CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
        if (cr != Win32.CR_BUFFER_SMALL || size <= 0) return new List<string>();
        var buf = new byte[size];
        if (Win32.CM_Get_DevNode_PropertyW(devInst, ref key, out _, buf, ref size, 0) != Win32.CR_SUCCESS)
            return new List<string>();
        var chars = Encoding.Unicode.GetString(buf, 0, size).ToCharArray();
        var list = Win32.ParseMultiSz(chars, chars.Length);
        if (list.Count == 0)
        {
            string single = new string(chars).TrimEnd('\0');
            if (single.Length > 0) list.Add(single);
        }
        return list;
    }

    private static string DeviceId(int devInst)
    {
        var buf = new char[512];
        return Win32.CM_Get_Device_IDW(devInst, buf, buf.Length, 0) == Win32.CR_SUCCESS
            ? new string(buf).TrimEnd('\0')
            : $"devinst:{devInst}";
    }

    private static string AsciiAt(byte[] buf, int offset)
    {
        if (offset <= 0 || offset >= buf.Length) return "";
        int end = Array.IndexOf(buf, (byte)0, offset);
        if (end < 0) end = buf.Length;
        return Encoding.ASCII.GetString(buf, offset, end - offset).Trim();
    }

    /// <summary>Bezbedno izbacivanje USB uređaja. Vraća null ako je uspelo, inače razlog.</summary>
    public static string? Eject(DiskInfo disk)
    {
        var veto = new char[260];
        int cr = Win32.CM_Request_Device_EjectW(disk.PortDevInst, out int vetoType, veto, veto.Length, 0);
        if (cr == Win32.CR_SUCCESS && vetoType == 0) return null;
        string name = new string(veto).TrimEnd('\0');
        return $"izbacivanje odbijeno (CR={cr}, veto={vetoType}{(name.Length > 0 ? ", " + name : "")})";
    }
}
