using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UsbCopyStation.Core;

/// <summary>Image spreman za upis: raw (.img/.bin) ili fixed VHD (raw + 512 B footer).</summary>
internal sealed class ImageInfo
{
    public string Path { get; init; } = "";
    public long FileLength { get; init; }
    /// <summary>Broj bajtova koji se upisuju na stik (bez VHD footer-a).</summary>
    public long PayloadLength { get; init; }
    public string Format { get; init; } = "";
    public FsKind FileSystem { get; init; }
    public bool HasMbr { get; init; }
    public string? Sha256 { get; set; }

    public static ImageInfo Inspect(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("Image ne postoji.", path);
        long len = fi.Length;
        if (len < 4096) throw new InvalidDataException("Fajl je premali da bi bio image diska.");

        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var head = new byte[8];
        RandomAccess.Read(h, head, 0);
        if (Encoding.ASCII.GetString(head) == "vhdxfile")
            throw new InvalidDataException("VHDX nije podržan. Koristi fixed VHD ili raw .img.");

        string format = "RAW";
        long payload = len;
        var footer = new byte[512];
        RandomAccess.Read(h, footer, len - 512);
        if (Encoding.ASCII.GetString(footer, 0, 8) == "conectix")
        {
            uint diskType = BinaryPrimitives.ReadUInt32BigEndian(footer.AsSpan(0x3C));
            if (diskType != 2)
                throw new InvalidDataException("Podržan je samo fixed VHD (ovo je dynamic ili differencing).");
            format = "VHD (fixed)";
            payload = len - 512;
        }

        if (payload % 512 != 0)
            throw new InvalidDataException("Veličina image-a nije deljiva sa 512 bajtova.");

        var layout = DiskLayout.Read(h, payload);
        return new ImageInfo
        {
            Path = path,
            FileLength = len,
            PayloadLength = payload,
            Format = format,
            FileSystem = layout.Fs,
            HasMbr = layout.HasMbr,
        };
    }

    public static string ComputeSha256(string path, long payloadLength, IProgress<double>? progress, CancellationToken ct)
    {
        using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[4 * 1024 * 1024];
        long off = 0;
        while (off < payloadLength)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(buf.Length, payloadLength - off);
            int r = RandomAccess.Read(h, buf.AsSpan(0, want), off);
            if (r <= 0) throw new EndOfStreamException();
            sha.AppendData(buf, 0, r);
            off += r;
            progress?.Report((double)off / payloadLength);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
