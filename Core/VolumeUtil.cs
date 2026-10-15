using Microsoft.Win32.SafeHandles;
using UsbCopyStation.Native;

namespace UsbCopyStation.Core;

/// <summary>Volumeni (particije sa fajl sistemom) i diskovi na kojima se nalaze.</summary>
internal static class VolumeUtil
{
    public sealed record VolumeEntry(string GuidPath, IReadOnlyList<int> Disks, IReadOnlyList<string> MountPoints);

    /// <summary>Svi volumeni u sistemu sa diskovima koje zauzimaju.</summary>
    public static List<VolumeEntry> EnumerateVolumes()
    {
        var result = new List<VolumeEntry>();
        var buf = new char[1024];
        IntPtr find = Win32.FindFirstVolumeW(buf, buf.Length);
        if (find == new IntPtr(-1)) return result;
        try
        {
            do
            {
                string guidPath = new string(buf).TrimEnd('\0'); // \\?\Volume{...}\
                var disks = DisksOfVolume(guidPath);
                if (disks.Count > 0)
                    result.Add(new VolumeEntry(guidPath, disks, MountPointsOf(guidPath)));
                Array.Clear(buf);
            } while (Win32.FindNextVolumeW(find, buf, buf.Length));
        }
        finally
        {
            Win32.FindVolumeClose(find);
        }
        return result;
    }

    public static List<int> DisksOfVolume(string volumeGuidPath)
    {
        var disks = new List<int>();
        using var h = Win32.TryOpen(volumeGuidPath.TrimEnd('\\'), 0);
        if (h is null) return disks;
        var outBuf = Win32.Ioctl(h, Win32.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 8 + 24 * 32);
        if (outBuf is null) return disks;
        int count = BitConverter.ToInt32(outBuf, 0);
        for (int i = 0; i < count && 8 + 24 * i + 4 <= outBuf.Length; i++)
        {
            int disk = BitConverter.ToInt32(outBuf, 8 + 24 * i);
            if (!disks.Contains(disk)) disks.Add(disk);
        }
        return disks;
    }

    public static List<string> MountPointsOf(string volumeGuidPath)
    {
        var buf = new char[2048];
        return Win32.GetVolumePathNamesForVolumeNameW(volumeGuidPath, buf, buf.Length, out int ret)
            ? Win32.ParseMultiSz(buf, ret)
            : new List<string>();
    }

    /// <summary>Diskovi na kojima leži data putanja (fajl ili folder). Prazno za mrežne putanje.</summary>
    public static List<int> DisksOfPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new List<int>();
        try
        {
            string full = Path.GetFullPath(path);
            var mp = new char[1024];
            if (!Win32.GetVolumePathNameW(full, mp, mp.Length)) return new List<int>();
            string mountPoint = new string(mp).TrimEnd('\0');
            if (!mountPoint.EndsWith('\\')) mountPoint += "\\";
            var vol = new char[1024];
            if (!Win32.GetVolumeNameForVolumeMountPointW(mountPoint, vol, vol.Length)) return new List<int>();
            return DisksOfVolume(new string(vol).TrimEnd('\0'));
        }
        catch
        {
            return new List<int>();
        }
    }

    /// <summary>
    /// Zaključava i demontira sve volumene na disku, da bi raw upis bio dozvoljen.
    /// Handle-ovi moraju ostati otvoreni do kraja upisa.
    /// </summary>
    public static List<SafeFileHandle> LockAndDismount(int diskNumber)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            foreach (var vol in EnumerateVolumes().Where(v => v.Disks.Contains(diskNumber)))
            {
                var h = Win32.Open(vol.GuidPath.TrimEnd('\\'), Win32.GENERIC_READ | Win32.GENERIC_WRITE);
                handles.Add(h);

                bool locked = TryLock(h, attempts: 10);
                if (!locked)
                {
                    // neko drži volumen otvorenim (Explorer, AV) — prisilni dismount poništava tuđe handle-ove
                    Win32.IoctlNoData(h, Win32.FSCTL_DISMOUNT_VOLUME);
                    locked = TryLock(h, attempts: 10);
                }
                if (!locked)
                    throw new IOException($"Volumen {string.Join(",", vol.MountPoints)} je u upotrebi i ne može da se zaključa.");

                if (!Win32.IoctlNoData(h, Win32.FSCTL_DISMOUNT_VOLUME))
                    throw new IOException($"Dismount volumena {string.Join(",", vol.MountPoints)} nije uspeo.");
            }
            return handles;
        }
        catch
        {
            Release(handles);
            throw;
        }
    }

    public static void Release(List<SafeFileHandle> handles)
    {
        foreach (var h in handles)
        {
            try { Win32.IoctlNoData(h, Win32.FSCTL_UNLOCK_VOLUME); } catch { /* ignore */ }
            h.Dispose();
        }
        handles.Clear();
    }

    private static bool TryLock(SafeFileHandle h, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (Win32.IoctlNoData(h, Win32.FSCTL_LOCK_VOLUME)) return true;
            Thread.Sleep(150);
        }
        return false;
    }
}
