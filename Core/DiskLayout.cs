using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace UsbCopyStation.Core;

internal enum FsKind { Unknown, Ntfs, Fat32, ExFat }

/// <summary>Izmena bajtova image-a na određenom offsetu, primenjuje se "u letu" tokom upisa i provere.</summary>
internal sealed record Patch(long Offset, byte[] Data);

/// <summary>
/// Raspored image-a: MBR + prva particija (ili "superfloppy" bez MBR-a) i njen fajl sistem.
/// Služi za prikaz i za jedinstven serijski broj po stiku.
/// </summary>
internal sealed class DiskLayout
{
    public FsKind Fs { get; private init; }
    public bool HasMbr { get; private init; }
    public long PartitionOffset { get; private init; }
    private byte[] _boot = new byte[512];

    public static string FsName(FsKind fs) => fs switch
    {
        FsKind.Ntfs => "NTFS",
        FsKind.Fat32 => "FAT32",
        FsKind.ExFat => "exFAT",
        _ => "nepoznat",
    };

    public static DiskLayout Read(SafeFileHandle image, long payloadLength)
    {
        var s0 = ReadBytes(image, 0, 512);
        var fs0 = Detect(s0);
        if (fs0 != FsKind.Unknown)
            return new DiskLayout { Fs = fs0, HasMbr = false, PartitionOffset = 0, _boot = s0 };

        if (s0[510] == 0x55 && s0[511] == 0xAA && s0[0x1C2] != 0)
        {
            long lba = BinaryPrimitives.ReadUInt32LittleEndian(s0.AsSpan(0x1C6));
            long off = lba * 512;
            if (off > 0 && off + 512 <= payloadLength)
            {
                var boot = ReadBytes(image, off, 512);
                return new DiskLayout { Fs = Detect(boot), HasMbr = true, PartitionOffset = off, _boot = boot };
            }
        }
        return new DiskLayout { Fs = FsKind.Unknown, HasMbr = s0[510] == 0x55 && s0[511] == 0xAA };
    }

    private static FsKind Detect(byte[] b)
    {
        string oem = Encoding.ASCII.GetString(b, 3, 8);
        if (oem == "NTFS    ") return FsKind.Ntfs;
        if (oem == "EXFAT   ") return FsKind.ExFat;
        if (Encoding.ASCII.GetString(b, 0x52, 8) == "FAT32   ") return FsKind.Fat32;
        return FsKind.Unknown;
    }

    /// <summary>
    /// Pravi skup izmena koje stiku daju sopstveni MBR disk signature i serijski broj volumena.
    /// Bez ovoga svi stikovi iz serije imaju isti potpis, pa Windows pri istovremenom priključenju
    /// dva takva stika jedan stavlja offline ("signature collision").
    /// </summary>
    public List<Patch> CreateUniquePatches(SafeFileHandle image, long payloadLength)
    {
        var patches = new List<Patch>();

        if (HasMbr)
            patches.Add(new Patch(0x1B8, RandomNonZero(4)));

        long p = PartitionOffset;
        switch (Fs)
        {
            case FsKind.Ntfs:
            {
                var serial = RandomNonZero(8);
                patches.Add(new Patch(p + 0x48, serial));
                int bps = BinaryPrimitives.ReadUInt16LittleEndian(_boot.AsSpan(0x0B));
                long total = BinaryPrimitives.ReadInt64LittleEndian(_boot.AsSpan(0x28));
                long backup = p + total * bps; // rezervni boot sektor je na kraju volumena
                if (bps >= 512 && backup + 512 <= payloadLength)
                    patches.Add(new Patch(backup + 0x48, serial));
                break;
            }
            case FsKind.Fat32:
            {
                var serial = RandomNonZero(4);
                patches.Add(new Patch(p + 0x43, serial));
                int bps = BinaryPrimitives.ReadUInt16LittleEndian(_boot.AsSpan(0x0B));
                int bk = BinaryPrimitives.ReadUInt16LittleEndian(_boot.AsSpan(0x32));
                if (bps >= 512 && bk != 0 && bk != 0xFFFF)
                    patches.Add(new Patch(p + (long)bk * bps + 0x43, serial));
                break;
            }
            case FsKind.ExFat:
            {
                // serijski broj je pokriven checksumom boot regiona (sektor 11) — mora da se preračuna
                var serial = RandomNonZero(4);
                int bps = 1 << _boot[0x6C];
                foreach (long region in new[] { p, p + 12L * bps }) // glavni i rezervni boot region
                {
                    if (region + 12L * bps > payloadLength) continue;
                    var data = ReadBytes(image, region, 12 * bps);
                    serial.CopyTo(data, 0x64);
                    uint sum = ExFatBootChecksum(data, bps);
                    var sumSector = new byte[bps];
                    for (int i = 0; i < bps; i += 4) BinaryPrimitives.WriteUInt32LittleEndian(sumSector.AsSpan(i), sum);
                    patches.Add(new Patch(region + 0x64, (byte[])serial.Clone()));
                    patches.Add(new Patch(region + 11L * bps, sumSector));
                }
                break;
            }
        }
        return patches;
    }

    private static uint ExFatBootChecksum(byte[] region, int bps)
    {
        uint sum = 0;
        int len = 11 * bps;
        for (int i = 0; i < len; i++)
        {
            if (i == 106 || i == 107 || i == 112) continue; // VolumeFlags i PercentInUse se ne računaju
            sum = ((sum & 1) != 0 ? 0x80000000u : 0u) + (sum >> 1) + region[i];
        }
        return sum;
    }

    public static void Apply(IReadOnlyList<Patch> patches, Span<byte> buffer, long bufferOffset)
    {
        long bufEnd = bufferOffset + buffer.Length;
        foreach (var patch in patches)
        {
            long pEnd = patch.Offset + patch.Data.Length;
            long start = Math.Max(patch.Offset, bufferOffset);
            long end = Math.Min(pEnd, bufEnd);
            if (start >= end) continue;
            patch.Data.AsSpan((int)(start - patch.Offset), (int)(end - start))
                .CopyTo(buffer.Slice((int)(start - bufferOffset)));
        }
    }

    private static byte[] RandomNonZero(int n)
    {
        var b = new byte[n];
        do RandomNumberGenerator.Fill(b); while (b.All(x => x == 0));
        return b;
    }

    private static byte[] ReadBytes(SafeFileHandle h, long offset, int count)
    {
        var b = new byte[count];
        int done = 0;
        while (done < count)
        {
            int r = RandomAccess.Read(h, b.AsSpan(done), offset + done);
            if (r <= 0) throw new EndOfStreamException("Image je kraći od očekivanog.");
            done += r;
        }
        return b;
    }
}
