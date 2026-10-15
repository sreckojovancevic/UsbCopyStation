using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UsbCopyStation.Core;

/// <summary>
/// Pravi fixed VHD od foldera: diskpart kreira i formatira virtuelni disk, fajlovi se kopiraju
/// preko privremene tačke montiranja, pa se VHD otkači. Fixed VHD = raw image + 512 B footer.
/// </summary>
internal static class ImageBuilder
{
    private const long MB = 1024 * 1024;

    public sealed record SizePlan(long SizeMb, long FileBytes, int FileCount, int DirCount, long LargestFile);

    public static SizePlan PlanSize(string folder, FsKind fs)
    {
        long cluster = fs == FsKind.Ntfs ? 4096 : 32768; // konzervativno za exFAT/FAT32
        long allocated = 0, bytes = 0, largest = 0;
        int files = 0, dirs = 0;

        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = 0 };
        foreach (var dir in Directory.EnumerateDirectories(folder, "*", opts)) { dirs++; allocated += cluster; }
        foreach (var path in Directory.EnumerateFiles(folder, "*", opts))
        {
            long len = new FileInfo(path).Length;
            files++;
            bytes += len;
            largest = Math.Max(largest, len);
            allocated += (len + cluster - 1) / cluster * cluster;
        }

        // metapodaci: MFT zapis ~1 KB po stavci kod NTFS-a, plus FAT tabele / log / rezerva
        long meta = fs == FsKind.Ntfs ? (files + dirs) * 1024L + 16 * MB : 8 * MB;
        long sizeMb = (long)Math.Ceiling((allocated * 1.06 + meta) / MB) + 16;
        sizeMb = Math.Max(sizeMb, 64);
        return new SizePlan(sizeMb, bytes, files, dirs, largest);
    }

    public static string Build(string folder, FsKind fs, string label, string workDir,
        Action<string> log, IProgress<double>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Izvorni folder ne postoji: {folder}");
        if (fs == FsKind.Unknown) throw new ArgumentException("Nije izabran fajl sistem.");

        var plan = PlanSize(folder, fs);
        log($"Izvor: {plan.FileCount:N0} fajlova, {plan.DirCount:N0} foldera, {plan.FileBytes / (double)MB:N1} MB → image {plan.SizeMb:N0} MB ({DiskLayout.FsName(fs)}).");

        if (fs == FsKind.Fat32)
        {
            if (plan.LargestFile >= 4L * 1024 * MB)
                throw new InvalidOperationException("FAT32 ne podržava fajlove veće od 4 GB. Izaberi NTFS ili exFAT.");
            if (plan.SizeMb > 32 * 1024)
                throw new InvalidOperationException("diskpart formatira FAT32 samo do 32 GB. Izaberi NTFS ili exFAT.");
        }

        Directory.CreateDirectory(workDir);
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(workDir))!).AvailableFreeSpace;
        if (free < (plan.SizeMb + 256) * MB)
            throw new IOException($"Nema dovoljno mesta u radnom folderu ({free / MB:N0} MB slobodno, treba ~{plan.SizeMb + 256:N0} MB).");

        string vhd = Path.Combine(Path.GetFullPath(workDir), "image.vhd");
        string mount = Path.Combine(Path.GetFullPath(workDir), "mnt-" + Guid.NewGuid().ToString("N")[..8]);

        if (File.Exists(vhd))
        {
            TryDetach(vhd, log);
            File.Delete(vhd);
        }
        Directory.CreateDirectory(mount);

        bool attached = false;
        try
        {
            string fsArg = fs switch { FsKind.Ntfs => "ntfs", FsKind.ExFat => "exfat", _ => "fat32" };
            RunDiskpart(new[]
            {
                $"create vdisk file=\"{vhd}\" maximum={plan.SizeMb} type=fixed",
                $"select vdisk file=\"{vhd}\"",
                "attach vdisk",
                "convert mbr",
                "create partition primary",
                $"format fs={fsArg} label=\"{SanitizeLabel(label, fs)}\" quick",
                $"assign mount=\"{mount}\"",
            }, log, "Kreiranje image-a");
            attached = true;

            log("Kopiranje fajlova u image...");
            CopyTree(folder, mount, plan.FileBytes, progress, ct);

            RunDiskpart(new[]
            {
                $"select vdisk file=\"{vhd}\"",
                "detach vdisk", // tačka montiranja ostaje kao prazan reparse folder i briše se ispod
            }, log, "Otkačinjanje image-a");
            attached = false;
        }
        catch
        {
            if (attached) TryDetach(vhd, log);
            try { if (File.Exists(vhd)) File.Delete(vhd); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            try { Directory.Delete(mount); } catch { /* ignore */ }
        }

        log($"Image je spreman: {vhd}");
        return vhd;
    }

    private static string SanitizeLabel(string label, FsKind fs)
    {
        int max = fs switch { FsKind.Ntfs => 32, FsKind.ExFat => 15, _ => 11 };
        var sb = new StringBuilder();
        foreach (char c in label.Trim())
        {
            if (c < 32 || "\"*/:<>?\\|+,.;=[]".IndexOf(c) >= 0) continue;
            sb.Append(fs == FsKind.Fat32 ? char.ToUpperInvariant(c) : c);
        }
        string s = sb.ToString();
        if (s.Length > max) s = s[..max];
        return s.Length == 0 ? "USB" : s;
    }

    private static void CopyTree(string src, string dst, long totalBytes, IProgress<double>? progress, CancellationToken ct)
    {
        long done = 0;
        var buffer = new byte[1024 * 1024];
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = 0 };

        foreach (var dir in Directory.EnumerateDirectories(src, "*", opts))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));

        foreach (var file in Directory.EnumerateFiles(src, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(dst, Path.GetRelativePath(src, file));
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
            {
                output.SetLength(input.Length);
                int r;
                while ((r = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, r);
                    done += r;
                    if (totalBytes > 0) progress?.Report((double)done / totalBytes);
                }
            }
            var info = new FileInfo(file);
            File.SetCreationTimeUtc(target, info.CreationTimeUtc);
            File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
            File.SetAttributes(target, info.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));
        }
    }

    private static void TryDetach(string vhd, Action<string> log)
    {
        try { RunDiskpart(new[] { $"select vdisk file=\"{vhd}\"", "detach vdisk" }, _ => { }, "detach"); }
        catch (Exception ex) { log($"Upozorenje: detach nije uspeo ({ex.Message})."); }
    }

    private static void RunDiskpart(string[] commands, Action<string> log, string what)
    {
        string script = Path.Combine(Path.GetTempPath(), $"ucs-diskpart-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(script, commands.Append("exit"), Encoding.Default);
        try
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo("diskpart.exe", $"/s \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = oem,
                StandardErrorEncoding = oem,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("diskpart nije pokrenut.");
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                string tail = string.Join(" | ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(4));
                throw new IOException($"{what}: diskpart greška {p.ExitCode}: {tail}");
            }
        }
        finally
        {
            try { File.Delete(script); } catch { /* ignore */ }
        }
    }
}
