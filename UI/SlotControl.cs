using UsbCopyStation.Core;

namespace UsbCopyStation.UI;

/// <summary>Kartica jednog slota (fizičkog porta) sa statusom i napretkom.</summary>
internal sealed class SlotControl : Panel
{
    private readonly Label _title = new() { AutoSize = false, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Label _port = new() { AutoSize = false, ForeColor = Color.FromArgb(90, 90, 90), Font = new Font("Segoe UI", 8f) };
    private readonly Label _device = new() { AutoSize = false, Font = new Font("Segoe UI", 8.5f) };
    private readonly Label _status = new() { AutoSize = false, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous };
    private readonly ToolTip _tip = new();

    public SlotState State { get; }
    public event Action<SlotControl, string>? CommandRequested;

    public SlotControl(SlotState state)
    {
        State = state;
        // kartica se pravi tokom rada, pa je forma ne skalira — skaliramo ručno po DPI-ju
        int D(int v) => LogicalToDeviceUnits(v);
        Size = new Size(D(270), D(128));
        Margin = new Padding(D(6));
        BorderStyle = BorderStyle.FixedSingle;
        DoubleBuffered = true;

        _title.SetBounds(D(8), D(6), D(110), D(24));
        _port.SetBounds(D(118), D(9), D(142), D(18));
        _port.TextAlign = ContentAlignment.TopRight;
        _device.SetBounds(D(8), D(32), D(254), D(36));
        _status.SetBounds(D(8), D(70), D(254), D(20));
        _bar.SetBounds(D(8), D(96), D(252), D(18));
        Controls.AddRange(new Control[] { _title, _port, _device, _status, _bar });

        var menu = new ContextMenuStrip();
        menu.Items.Add("Upiši ovaj stik", null, (_, _) => CommandRequested?.Invoke(this, "write"));
        menu.Items.Add("Otkaži", null, (_, _) => CommandRequested?.Invoke(this, "cancel"));
        menu.Items.Add("Izbaci stik", null, (_, _) => CommandRequested?.Invoke(this, "eject"));
        ContextMenuStrip = menu;
        foreach (Control c in Controls) c.ContextMenuStrip = menu;
    }

    public void Render()
    {
        var s = State;
        var d = s.Disk;
        _title.Text = $"Slot {s.Number}";
        _port.Text = d?.LocationInfo is { Length: > 0 } li ? li : "";

        _device.Text = d is null || !s.Present && !s.Running
            ? "— prazno —"
            : $"Disk {d.DiskNumber} · {d.Model}\n{d.Size / 1e9:0.0} GB{(d.VolumeMountPoints.Count > 0 ? " · " + string.Join(" ", d.VolumeMountPoints.Where(m => m.Length <= 3)) : "")}";

        string tip = d is null ? s.Key : $"Port: {d.LocationPath}\nInstanca: {d.InstanceId}\nSerijski: {d.Serial}";
        if (_tip.GetToolTip(this) != tip)
            foreach (Control c in Controls.Cast<Control>().Append(this)) _tip.SetToolTip(c, tip);

        (string text, Color back) = s.Phase switch
        {
            SlotPhase.Empty => ("Prazno", Color.FromArgb(235, 235, 235)),
            SlotPhase.Ready => ("Spreman", Color.White),
            SlotPhase.HasData => ("Ima podatke", Color.FromArgb(255, 240, 200)),
            SlotPhase.Blocked => (s.BlockReason, Color.FromArgb(225, 225, 225)),
            SlotPhase.Waiting => ("Čeka red...", Color.FromArgb(220, 235, 255)),
            SlotPhase.Writing => ($"Upis {s.Fraction:P0} · {s.MBps:0.0} MB/s", Color.FromArgb(190, 220, 255)),
            SlotPhase.Verifying => ($"Provera {s.Fraction:P0} · {s.MBps:0.0} MB/s", Color.FromArgb(220, 205, 255)),
            SlotPhase.Done => ($"OK · {Fmt(s.LastDuration)}", Color.FromArgb(185, 235, 185)),
            SlotPhase.Failed => ("GREŠKA", Color.FromArgb(255, 190, 190)),
            SlotPhase.Cancelled => ("Otkazano", Color.FromArgb(255, 215, 180)),
            _ => ("", Color.White),
        };
        if (!s.Present && s.Phase is SlotPhase.Done or SlotPhase.Failed or SlotPhase.Cancelled)
            text += " (izvađen)";

        _status.Text = text;
        if (BackColor != back) BackColor = back;

        int value = s.Phase switch
        {
            SlotPhase.Writing or SlotPhase.Verifying => (int)(s.Fraction * 1000),
            SlotPhase.Done => 1000,
            _ => 0,
        };
        if (_bar.Value != value) _bar.Value = value;

        string detail = s.Phase is SlotPhase.Failed or SlotPhase.Cancelled or SlotPhase.Done ? s.Message : "";
        if (_tip.GetToolTip(_status) != (detail.Length > 0 ? detail : tip))
            _tip.SetToolTip(_status, detail.Length > 0 ? detail : tip);
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
