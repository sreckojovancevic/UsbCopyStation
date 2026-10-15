using Microsoft.Win32.SafeHandles;
using UsbCopyStation.Native;

namespace UsbCopyStation.Core;

internal sealed record JobOptions(bool Verify, bool UniqueSerial);

internal sealed record JobResult(TimeSpan WriteTime, TimeSpan VerifyTime, double WriteMBps);

/// <summary>Raw upis image-a na jedan fizički disk + provera čitanjem direktno sa uređaja.</summary>
internal static unsafe class WriteJob
{
    private const int Chunk = 4 * 1024 * 1024;

    public static JobResult Run(DiskInfo disk, ImageInfo image, JobOptions opt, SlotState state, CancellationToken ct)
    {
        int sector = Math.Max(512, disk.SectorSize);
        long payload = image.PayloadLength;
        if (RoundUp(payload, sector) > disk.Size)
            throw new IOException("Image je veći od stika.");

        using var diskH = Win32.Open($@"\\.\PhysicalDrive{disk.DiskNumber}",
            Win32.GENERIC_READ | Win32.GENERIC_WRITE,
            Win32.FILE_FLAG_NO_BUFFERING | Win32.FILE_FLAG_WRITE_THROUGH);

        // zaštita od trke: broj diska je mogao da se promeni između skeniranja i starta
        if (DiskEnumerator.GetDiskNumber(diskH) != disk.DiskNumber)
            throw new IOException("Broj diska se promenio — stik je verovatno zamenjen.");

        var locks = VolumeUtil.LockAndDismount(disk.DiskNumber);
        try
        {
            using var img = File.OpenHandle(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
            var patches = opt.UniqueSerial
                ? DiskLayout.Read(img, payload).CreateUniquePatches(img, payload)
                : new List<Patch>();

            using var src = new AlignedBuffer(Chunk);
            using var dev = opt.Verify ? new AlignedBuffer(Chunk) : null;

            // ---------- upis ----------
            state.BeginPhase(SlotPhase.Writing, payload);
            var t0 = DateTime.UtcNow;
            for (long off = 0; off < payload; off += Chunk)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Chunk, payload - off);
                int nAligned = (int)RoundUp(n, sector);
                FillFromImage(img, src, n, nAligned, off, patches);
                Win32.WriteAt(diskH, src.Pointer, nAligned, off);
                state.Advance(n);
            }
            if (!Win32.FlushFileBuffers(diskH))
                throw new IOException("Flush na uređaj nije uspeo.");
            var writeTime = DateTime.UtcNow - t0;

            // ---------- provera ----------
            var verifyTime = TimeSpan.Zero;
            if (dev is not null)
            {
                state.BeginPhase(SlotPhase.Verifying, payload);
                var t1 = DateTime.UtcNow;
                for (long off = 0; off < payload; off += Chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(Chunk, payload - off);
                    int nAligned = (int)RoundUp(n, sector);
                    FillFromImage(img, src, n, nAligned, off, patches);
                    Win32.ReadAt(diskH, dev.Pointer, nAligned, off); // NO_BUFFERING: čita sa flash-a, ne iz keša
                    var a = src.Span[..n];
                    var b = dev.Span[..n];
                    if (!a.SequenceEqual(b))
                    {
                        int i = 0;
                        while (i < n && a[i] == b[i]) i++;
                        throw new IOException($"Provera nije prošla: razlika na offsetu {off + i:N0}. Stik je neispravan ili lažnog kapaciteta.");
                    }
                    state.Advance(n);
                }
                verifyTime = DateTime.UtcNow - t1;
            }

            double mbps = writeTime.TotalSeconds > 0 ? payload / writeTime.TotalSeconds / (1024 * 1024) : 0;
            return new JobResult(writeTime, verifyTime, mbps);
        }
        finally
        {
            VolumeUtil.Release(locks);
            Win32.IoctlNoData(diskH, Win32.IOCTL_DISK_UPDATE_PROPERTIES); // Windows ponovo čita tabelu particija
        }
    }

    private static void FillFromImage(SafeFileHandle img, AlignedBuffer buf, int n, int nAligned, long off, List<Patch> patches)
    {
        var span = buf.Span;
        int done = 0;
        while (done < n)
        {
            int r = RandomAccess.Read(img, span.Slice(done, n - done), off + done);
            if (r <= 0) throw new EndOfStreamException("Image je kraći od očekivanog.");
            done += r;
        }
        if (nAligned > n) span.Slice(n, nAligned - n).Clear();
        if (patches.Count > 0) DiskLayout.Apply(patches, span[..n], off);
    }

    private static long RoundUp(long value, int multiple) => (value + multiple - 1) / multiple * multiple;
}
