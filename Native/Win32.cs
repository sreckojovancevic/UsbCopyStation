using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UsbCopyStation.Native;

internal static unsafe class Win32
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    public const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    public const uint FSCTL_LOCK_VOLUME = 0x00090018;
    public const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
    public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

    public const int STORAGE_BUS_TYPE_USB = 7;

    public const int DIGCF_PRESENT = 0x2;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const int CR_SUCCESS = 0;
    public const int CR_BUFFER_SMALL = 0x1A;

    public const int WM_DEVICECHANGE = 0x0219;
    public const int DBT_DEVNODES_CHANGED = 0x0007;
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const int DBT_DEVTYP_DEVICEINTERFACE = 5;

    public static readonly Guid GUID_DEVINTERFACE_DISK = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    // ---------------- kernel32 ----------------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, byte[]? lpInBuffer,
        int nInBufferSize, byte[]? lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle h, byte* buffer, int count, out int read, NativeOverlapped* ov);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle h, byte* buffer, int count, out int written, NativeOverlapped* ov);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstVolumeW(char[] volumeName, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextVolumeW(IntPtr hFind, char[] volumeName, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindVolumeClose(IntPtr hFind);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNamesForVolumeNameW(string volumeName, char[] names, int length, out int returned);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNameW(string fileName, char[] volumePath, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, char[] volumeName, int length);

    // ---------------- setupapi ----------------

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid interfaceClassGuid,
        int memberIndex, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
        IntPtr detail, int detailSize, out int requiredSize, ref SP_DEVINFO_DATA devInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    // ---------------- cfgmgr32 ----------------

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
        public DEVPROPKEY(string guid, uint id) { fmtid = new Guid(guid); pid = id; }
    }

    public static readonly DEVPROPKEY DEVPKEY_Device_LocationInfo = new("a45c254e-df1c-4efd-8020-67d146a850e0", 15);
    public static readonly DEVPROPKEY DEVPKEY_Device_LocationPaths = new("a45c254e-df1c-4efd-8020-67d146a850e0", 37);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out int parent, int devInst, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_IDW(int devInst, char[] buffer, int length, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_DevNode_PropertyW(int devInst, ref DEVPROPKEY key, out uint propertyType,
        byte[]? buffer, ref int bufferSize, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Request_Device_EjectW(int devInst, out int vetoType, char[]? vetoName, int nameLength, int flags);

    // ---------------- user32 ----------------

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, IntPtr filter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterDeviceNotification(IntPtr handle);

    // ---------------- pomoćne funkcije ----------------

    public static string LastErrorText(int code) => $"{new Win32Exception(code).Message} (0x{code:X8})";

    public static SafeFileHandle Open(string path, uint access, uint flags = 0)
    {
        var h = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            throw new IOException($"Ne mogu da otvorim {path}: {LastErrorText(err)}");
        }
        return h;
    }

    public static SafeFileHandle? TryOpen(string path, uint access, uint flags = 0)
    {
        var h = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        h.Dispose();
        return null;
    }

    /// <summary>IOCTL koji vraća izlazni bafer ili null ako nije uspeo.</summary>
    public static byte[]? Ioctl(SafeFileHandle h, uint code, byte[]? input, int outSize)
    {
        var output = outSize > 0 ? new byte[outSize] : null;
        return DeviceIoControl(h, code, input, input?.Length ?? 0, output, outSize, out _, IntPtr.Zero)
            ? output ?? Array.Empty<byte>()
            : null;
    }

    public static bool IoctlNoData(SafeFileHandle h, uint code) =>
        DeviceIoControl(h, code, null, 0, null, 0, out _, IntPtr.Zero);

    public static void ReadAt(SafeFileHandle h, byte* buffer, int count, long offset)
    {
        var ov = new NativeOverlapped { OffsetLow = unchecked((int)(offset & 0xFFFFFFFF)), OffsetHigh = (int)(offset >> 32) };
        if (!ReadFile(h, buffer, count, out int read, &ov))
            throw new IOException($"Čitanje sa uređaja na offsetu {offset:N0} nije uspelo: {LastErrorText(Marshal.GetLastWin32Error())}");
        if (read != count)
            throw new IOException($"Kratko čitanje na offsetu {offset:N0} ({read} od {count} bajtova).");
    }

    public static void WriteAt(SafeFileHandle h, byte* buffer, int count, long offset)
    {
        var ov = new NativeOverlapped { OffsetLow = unchecked((int)(offset & 0xFFFFFFFF)), OffsetHigh = (int)(offset >> 32) };
        if (!WriteFile(h, buffer, count, out int written, &ov))
            throw new IOException($"Upis na offset {offset:N0} nije uspeo: {LastErrorText(Marshal.GetLastWin32Error())}");
        if (written != count)
            throw new IOException($"Kratak upis na offsetu {offset:N0} ({written} od {count} bajtova).");
    }

    public static List<string> ParseMultiSz(char[] buffer, int length)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < Math.Min(length, buffer.Length); i++)
        {
            if (buffer[i] != '\0') continue;
            if (i == start) break;
            list.Add(new string(buffer, start, i - start));
            start = i + 1;
        }
        return list;
    }
}
