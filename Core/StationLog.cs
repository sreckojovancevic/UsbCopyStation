using System.Globalization;
using System.Text;

namespace UsbCopyStation.Core;

/// <summary>CSV evidencija svakog upisanog stika (jedan fajl po danu).</summary>
internal sealed class StationLog
{
    private readonly object _lock = new();
    public string Folder { get; } = Path.Combine(Settings.AppDir, "logs");

    public void Record(SlotState slot, DiskInfo disk, ImageInfo? image, string result, string message, JobResult? r)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, $"{DateTime.Now:yyyy-MM-dd}.csv");
                bool header = !File.Exists(file);
                var sb = new StringBuilder();
                if (header)
                    sb.AppendLine("vreme;slot;port;disk;model;serijski;velicina_gb;image;image_sha256;rezultat;upis_s;provera_s;mb_s;poruka");
                var ci = CultureInfo.InvariantCulture;
                sb.AppendLine(string.Join(";",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci),
                    slot.Number.ToString(ci),
                    Csv(disk.LocationInfo.Length > 0 ? disk.LocationInfo : disk.LocationPath),
                    disk.DiskNumber.ToString(ci),
                    Csv(disk.Model),
                    Csv(disk.Serial),
                    (disk.Size / 1e9).ToString("0.0", ci),
                    Csv(image is null ? "" : Path.GetFileName(image.Path)),
                    image?.Sha256 ?? "",
                    result,
                    r?.WriteTime.TotalSeconds.ToString("0", ci) ?? "",
                    r?.VerifyTime.TotalSeconds.ToString("0", ci) ?? "",
                    r?.WriteMBps.ToString("0.0", ci) ?? "",
                    Csv(message)));
                File.AppendAllText(file, sb.ToString(), new UTF8Encoding(header));
            }
            catch { /* log ne sme da obori stanicu */ }
        }
    }

    private static string Csv(string s) =>
        s.IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"").Replace('\n', ' ') + "\"" : s;
}
