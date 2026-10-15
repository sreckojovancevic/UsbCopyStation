using System.Diagnostics;
using System.Runtime.InteropServices;
using UsbCopyStation.Core;
using UsbCopyStation.Native;

namespace UsbCopyStation.UI;

internal sealed class MainForm : Form
{
    // ---- izvor ----
    private readonly RadioButton _rbFolder = new() { Text = "Folder → napravi image", AutoSize = true };
    private readonly RadioButton _rbImage = new() { Text = "Gotov image (.img / .vhd)", AutoSize = true };
    private readonly TextBox _txtFolder = new() { Width = 360 };
    private readonly TextBox _txtImage = new() { Width = 360 };
    private readonly TextBox _txtWork = new() { Width = 360 };
    private readonly ComboBox _cmbFs = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly TextBox _txtLabel = new() { Width = 110 };
    private readonly Button _btnPrepare = new() { Text = "Pripremi image", AutoSize = true };
    private readonly Label _lblImage = new() { AutoSize = true, Text = "Image nije pripremljen.", ForeColor = Color.DarkRed };

    // ---- opcije ----
    private readonly ComboBox _cmbMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly CheckBox _chkVerify = new() { Text = "Provera posle upisa", AutoSize = true };
    private readonly CheckBox _chkUnique = new() { Text = "Jedinstven serijski broj", AutoSize = true };
    private readonly CheckBox _chkAllowData = new() { Text = "Dozvoli stikove sa podacima", AutoSize = true };
    private readonly CheckBox _chkEject = new() { Text = "Izbaci stik kad završi", AutoSize = true };
    private readonly NumericUpDown _numMaxGb = new() { Minimum = 1, Maximum = 16384, Width = 70 };
    private readonly NumericUpDown _numParallel = new() { Minimum = 1, Maximum = 64, Width = 50 };

    // ---- komande ----
    private readonly Button _btnStart = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly Button _btnStop = new() { Text = "Stop sve", AutoSize = true };
    private readonly Button _btnRescan = new() { Text = "Osveži", AutoSize = true };
    private readonly Button _btnResetSlots = new() { Text = "Resetuj slotove", AutoSize = true };
    private readonly Button _btnLogs = new() { Text = "Log folder", AutoSize = true };
    private readonly Label _lblSummary = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };

    private readonly FlowLayoutPanel _slotPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(248, 248, 248) };
    private readonly ListBox _logBox = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, Font = new Font("Consolas", 9f) };

    // ---- stanje ----
    private readonly Settings _settings = Settings.Load();
    private readonly StationLog _stationLog = new();
    private readonly Dictionary<string, SlotState> _slots = new();
    private readonly Dictionary<string, SlotControl> _slotControls = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _scanTimer = new();
    private SemaphoreSlim _parallel = new(16);
    private HashSet<int> _excludedDisks = new();
    private ImageInfo? _image;
    private bool _armed;
    private bool _scanning, _scanAgain;
    private bool _preparing;
    private int _okCount, _failCount;
    private IntPtr _devNotify;

    public MainForm()
    {
        SuspendLayout();
        Text = "USB Copy Station";
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* bez ikonice */ }
        MinimumSize = new Size(1020, 700);
        Size = new Size(1240, 860);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        // skaliranje na 125/150% DPI (forma je pravljena u kodu, bez designer-a)
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
        LoadSettingsToUi();
        WireEvents();

        _uiTimer.Tick += (_, _) => RenderSlots();
        _uiTimer.Start();
        _scanTimer.Tick += (_, _) => { _scanTimer.Stop(); _ = DoScanAsync(); };
    }

    // =====================================================================
    // Raspored
    // =====================================================================

    private void BuildLayout()
    {
        _cmbFs.Items.AddRange(new object[] { "NTFS", "exFAT", "FAT32" });
        _cmbMode.Items.AddRange(new object[] { "Ručno (Start piše sve utaknute)", "Automatski (utakni → piši)" });

        var btnFolder = new Button { Text = "...", Width = 32 };
        btnFolder.Click += (_, _) => BrowseFolder(_txtFolder);
        var btnImage = new Button { Text = "...", Width = 32 };
        btnImage.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Disk image (*.img;*.vhd;*.bin)|*.img;*.vhd;*.bin|Svi fajlovi|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _txtImage.Text = dlg.FileName;
        };
        var btnWork = new Button { Text = "...", Width = 32 };
        btnWork.Click += (_, _) => BrowseFolder(_txtWork);

        var source = Group("Izvor",
            Row(_rbFolder, _txtFolder, btnFolder, Lbl("FS:"), _cmbFs, Lbl("Labela:"), _txtLabel),
            Row(_rbImage, _txtImage, btnImage),
            Row(Lbl("Radni folder (temp image):"), _txtWork, btnWork),
            Row(_btnPrepare, _lblImage));

        var options = Group("Opcije",
            Row(Lbl("Režim:"), _cmbMode, Lbl("Maks. stik GB:"), _numMaxGb, Lbl("Paralelno:"), _numParallel),
            Row(_chkVerify, _chkUnique, _chkAllowData, _chkEject));

        var commands = Row(_btnStart, _btnStop, _btnRescan, _btnResetSlots, _btnLogs, _lblSummary);
        commands.Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        root.Controls.Add(source, 0, 0);
        root.Controls.Add(options, 0, 1);
        root.Controls.Add(commands, 0, 2);
        root.Controls.Add(_slotPanel, 0, 3);
        root.Controls.Add(_logBox, 0, 4);
        Controls.Add(root);
    }

    /// <summary>Jedan red kontrola: tabela sa jednim redom, sve kontrole vertikalno centrirane, bez prelamanja.</summary>
    private static TableLayoutPanel Row(params Control[] controls)
    {
        var t = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 1,
            ColumnCount = controls.Length,
            Margin = new Padding(0, 1, 0, 1),
        };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < controls.Length; i++)
        {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var c = controls[i];
            c.Anchor = AnchorStyles.Left; // samo Left = centrirano po visini reda
            c.Margin = new Padding(3, 3, 8, 3);
            t.Controls.Add(c, i, 0);
        }
        return t;
    }

    private static GroupBox Group(string title, params Control[] rows)
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        stack.Controls.AddRange(rows);
        var g = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 6),
        };
        g.Controls.Add(stack);
        return g;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true };

    private void BrowseFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = target.Text, UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    private void LoadSettingsToUi()
    {
        var s = _settings;
        _txtFolder.Text = s.SourceFolder;
        _txtImage.Text = s.ImagePath;
        _txtWork.Text = s.WorkDir;
        _rbFolder.Checked = s.UseFolder;
        _rbImage.Checked = !s.UseFolder;
        _cmbFs.SelectedItem = _cmbFs.Items.Contains(s.FileSystem) ? s.FileSystem : "NTFS";
        _txtLabel.Text = s.Label;
        _cmbMode.SelectedIndex = s.AutoMode ? 1 : 0;
        _chkVerify.Checked = s.Verify;
        _chkUnique.Checked = s.UniqueSerial;
        _chkAllowData.Checked = s.AllowOverwriteData;
        _chkEject.Checked = s.EjectWhenDone;
        _numMaxGb.Value = Math.Clamp(s.MaxSizeGb, 1, 16384);
        _numParallel.Value = Math.Clamp(s.MaxParallel, 1, 64);
        _parallel = new SemaphoreSlim((int)_numParallel.Value);
        UpdateSourceEnabled();
        UpdateStartButton();
    }

    private void SaveUiToSettings()
    {
        var s = _settings;
        s.SourceFolder = _txtFolder.Text.Trim();
        s.ImagePath = _txtImage.Text.Trim();
        s.WorkDir = _txtWork.Text.Trim();
        s.UseFolder = _rbFolder.Checked;
        s.FileSystem = _cmbFs.SelectedItem?.ToString() ?? "NTFS";
        s.Label = _txtLabel.Text.Trim();
        s.AutoMode = _cmbMode.SelectedIndex == 1;
        s.Verify = _chkVerify.Checked;
        s.UniqueSerial = _chkUnique.Checked;
        s.AllowOverwriteData = _chkAllowData.Checked;
        s.EjectWhenDone = _chkEject.Checked;
        s.MaxSizeGb = (int)_numMaxGb.Value;
        s.MaxParallel = (int)_numParallel.Value;
        s.Save();
    }

    private void WireEvents()
    {
        _rbFolder.CheckedChanged += (_, _) => UpdateSourceEnabled();
        _btnPrepare.Click += async (_, _) => await PrepareImageAsync();
        _btnStart.Click += (_, _) => OnStartClicked();
        _btnStop.Click += (_, _) => StopAll();
        _btnRescan.Click += (_, _) => RequestScan(10);
        _btnResetSlots.Click += (_, _) => ResetSlots();
        _btnLogs.Click += (_, _) =>
        {
            Directory.CreateDirectory(_stationLog.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_stationLog.Folder}\"") { UseShellExecute = true });
        };
        _cmbMode.SelectedIndexChanged += (_, _) => { if (_armed) Disarm(); UpdateStartButton(); };
        _chkAllowData.CheckedChanged += (_, _) => RequestScan(10);
        _numMaxGb.ValueChanged += (_, _) => RequestScan(300);
        _numParallel.ValueChanged += (_, _) =>
        {
            if (_slots.Values.Any(s => s.Running))
                Log("Broj paralelnih upisa se primenjuje kad se završe tekući poslovi.");
            else
                _parallel = new SemaphoreSlim((int)_numParallel.Value);
        };
    }

    private void UpdateSourceEnabled()
    {
        bool folder = _rbFolder.Checked;
        _txtFolder.Enabled = _cmbFs.Enabled = _txtLabel.Enabled = folder;
        _txtImage.Enabled = !folder;
    }

    private void UpdateStartButton()
    {
        bool auto = _cmbMode.SelectedIndex == 1;
        _btnStart.Text = auto ? (_armed ? "■ Isključi automatski" : "▶ Uključi automatski") : "▶ Start (svi utaknuti)";
        _btnStart.BackColor = _armed ? Color.FromArgb(185, 235, 185) : SystemColors.Control;
    }

    // =====================================================================
    // Hotplug
    // =====================================================================

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // obaveštenja o dolasku/odlasku disk interfejsa (i za stikove bez particija)
        IntPtr filter = Marshal.AllocHGlobal(32);
        try
        {
            for (int i = 0; i < 32; i += 4) Marshal.WriteInt32(filter, i, 0);
            Marshal.WriteInt32(filter, 0, 32);
            Marshal.WriteInt32(filter, 4, Win32.DBT_DEVTYP_DEVICEINTERFACE);
            Marshal.Copy(Win32.GUID_DEVINTERFACE_DISK.ToByteArray(), 0, filter + 12, 16);
            _devNotify = Win32.RegisterDeviceNotificationW(Handle, filter, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(filter);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Log("Stanica pokrenuta. Pripremi image, pa utakni stikove.");
        RequestScan(10);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_DEVICECHANGE)
        {
            int evt = m.WParam.ToInt32();
            if (evt == Win32.DBT_DEVICEREMOVECOMPLETE && m.LParam != IntPtr.Zero &&
                Marshal.ReadInt32(m.LParam, 4) == Win32.DBT_DEVTYP_DEVICEINTERFACE)
            {
                string path = Marshal.PtrToStringUni(m.LParam + 28) ?? "";
                var slot = _slots.Values.FirstOrDefault(s =>
                    s.Present && string.Equals(s.Disk?.DevicePath, path, StringComparison.OrdinalIgnoreCase));
                if (slot is not null) MarkRemoved(slot);
                RequestScan(300);
            }
            else if (evt is Win32.DBT_DEVICEARRIVAL or Win32.DBT_DEVNODES_CHANGED or Win32.DBT_DEVICEREMOVECOMPLETE)
            {
                RequestScan(800);
            }
        }
        base.WndProc(ref m);
    }

    private void RequestScan(int delayMs)
    {
        _scanTimer.Stop();
        _scanTimer.Interval = Math.Max(1, delayMs);
        _scanTimer.Start();
    }

    private async Task DoScanAsync()
    {
        if (_scanning) { _scanAgain = true; return; }
        _scanning = true;
        try
        {
            var excludedPaths = new List<string?>
            {
                Environment.SystemDirectory,
                AppContext.BaseDirectory,
                _txtWork.Text,
                _image?.Path,
                _rbFolder.Checked ? _txtFolder.Text : _txtImage.Text,
            };
            var (disks, excluded) = await Task.Run(() =>
            {
                var d = DiskEnumerator.EnumerateDisks();
                var ex = new HashSet<int>(excludedPaths.SelectMany(VolumeUtil.DisksOfPath));
                return (d, ex);
            });
            _excludedDisks = excluded;
            Reconcile(disks);
        }
        catch (Exception ex)
        {
            Log($"Greška pri skeniranju uređaja: {ex.Message}");
        }
        finally
        {
            _scanning = false;
            if (_scanAgain) { _scanAgain = false; RequestScan(300); }
        }
    }

    private void Reconcile(List<DiskInfo> disks)
    {
        var seen = new HashSet<string>();
        bool layoutChanged = false;

        foreach (var d in disks.Where(d => d.IsUsb))
        {
            string key = d.SlotKey;
            if (!seen.Add(key)) continue; // dva diska na istom portu (npr. multi-LUN čitač) — samo prvi
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = CreateSlot(key);
                layoutChanged = true;
            }

            if (slot.Running)
            {
                slot.Present = true;
                continue;
            }

            bool newStick = slot.Disk is null || slot.Disk.InstanceId != d.InstanceId || !slot.Present;
            if (newStick && slot.Disk is not null && slot.Disk.InstanceId != d.InstanceId)
                slot.DoneInstanceId = null;
            slot.Disk = d;
            slot.Present = true;
            UpdateIdlePhase(slot);
        }

        foreach (var slot in _slots.Values.Where(s => !seen.Contains(s.Key) && s.Present))
            MarkRemoved(slot);

        if (layoutChanged) LayoutSlots();
        if (_armed) StartAuto();
    }

    private SlotState CreateSlot(string key)
    {
        if (!_settings.SlotMap.TryGetValue(key, out int number))
        {
            number = _settings.SlotMap.Count == 0 ? 1 : _settings.SlotMap.Values.Max() + 1;
            _settings.SlotMap[key] = number;
            _settings.Save();
        }
        var slot = new SlotState { Key = key, Number = number };
        _slots[key] = slot;
        var ctl = new SlotControl(slot);
        ctl.CommandRequested += OnSlotCommand;
        _slotControls[key] = ctl;
        return slot;
    }

    private void LayoutSlots()
    {
        _slotPanel.SuspendLayout();
        _slotPanel.Controls.Clear();
        foreach (var ctl in _slotControls.Values.OrderBy(c => c.State.Number))
            _slotPanel.Controls.Add(ctl);
        _slotPanel.ResumeLayout();
    }

    private void MarkRemoved(SlotState slot)
    {
        slot.Present = false;
        slot.DoneInstanceId = null;
        if (slot.Running)
        {
            slot.Cts?.Cancel();
            return;
        }
        if (slot.Phase is not (SlotPhase.Done or SlotPhase.Failed or SlotPhase.Cancelled))
        {
            slot.Phase = SlotPhase.Empty;
            slot.Disk = null;
        }
        slot.LastJobInstanceId = null;
    }

    private string? BlockReasonFor(DiskInfo d)
    {
        if (_excludedDisks.Contains(d.DiskNumber)) return "Izuzet (sistem/izvor)";
        if (_image is null) return "Čeka image";
        if (d.Size < _image.PayloadLength) return $"Premali (< {_image.PayloadLength / 1e9:0.0} GB)";
        if (d.Size > (long)_numMaxGb.Value * 1_000_000_000L) return $"Prevelik (> {_numMaxGb.Value} GB)";
        return null;
    }

    private void UpdateIdlePhase(SlotState slot)
    {
        var d = slot.Disk!;
        if (slot.LastJobInstanceId == d.InstanceId &&
            slot.Phase is SlotPhase.Done or SlotPhase.Failed or SlotPhase.Cancelled)
            return; // rezultat ostaje vidljiv dok je isti stik u portu

        string? reason = BlockReasonFor(d);
        if (reason is not null)
        {
            slot.BlockReason = reason;
            slot.Phase = SlotPhase.Blocked;
        }
        else if (d.HasVolumes && !_chkAllowData.Checked)
        {
            slot.Phase = SlotPhase.HasData;
        }
        else
        {
            slot.Phase = SlotPhase.Ready;
        }
    }

    // =====================================================================
    // Image
    // =====================================================================

    private async Task PrepareImageAsync()
    {
        if (_slots.Values.Any(s => s.Running))
        {
            MessageBox.Show(this, "Sačekaj da se završe tekući upisi.", Text);
            return;
        }
        if (_armed) Disarm();

        SaveUiToSettings();
        _preparing = true;
        _btnPrepare.Enabled = false;
        _image = null;
        _lblImage.ForeColor = Color.DarkBlue;
        RequestScan(10);

        try
        {
            string path;
            if (_rbFolder.Checked)
            {
                string folder = _txtFolder.Text.Trim();
                var fs = (_cmbFs.SelectedItem?.ToString()) switch { "exFAT" => FsKind.ExFat, "FAT32" => FsKind.Fat32, _ => FsKind.Ntfs };
                string label = _txtLabel.Text, work = _txtWork.Text.Trim();
                if (string.IsNullOrEmpty(work)) throw new InvalidOperationException("Radni folder nije zadat.");
                if (Path.GetFullPath(work).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Radni folder ne sme biti unutar izvornog foldera.");

                _lblImage.Text = "Pravim image...";
                var progress = new Progress<double>(f => _lblImage.Text = $"Kopiranje u image {f:P0}");
                path = await Task.Run(() => ImageBuilder.Build(folder, fs, label, work, LogFromAnyThread, progress, CancellationToken.None));
            }
            else
            {
                path = _txtImage.Text.Trim();
            }

            var info = await Task.Run(() => ImageInfo.Inspect(path));
            var hashProgress = new Progress<double>(f => _lblImage.Text = $"SHA-256 {f:P0}");
            info.Sha256 = await Task.Run(() => ImageInfo.ComputeSha256(path, info.PayloadLength, hashProgress, CancellationToken.None));
            _image = info;

            _lblImage.ForeColor = Color.DarkGreen;
            _lblImage.Text = $"{Path.GetFileName(info.Path)} · {info.Format} · {DiskLayout.FsName(info.FileSystem)} · " +
                             $"{info.PayloadLength / 1048576.0:N0} MB · SHA-256 {info.Sha256[..16]}…";
            Log($"Image spreman: {info.Path} ({info.PayloadLength:N0} B, {DiskLayout.FsName(info.FileSystem)}, sha256 {info.Sha256})");
            if (info.FileSystem == FsKind.Unknown && _chkUnique.Checked)
                Log("Napomena: fajl sistem image-a nije prepoznat — menja se samo MBR potpis, ne serijski broj volumena.");
        }
        catch (Exception ex)
        {
            _image = null;
            _lblImage.ForeColor = Color.DarkRed;
            _lblImage.Text = "Image nije pripremljen.";
            Log($"GREŠKA (image): {ex.Message}");
            MessageBox.Show(this, ex.Message, "Priprema image-a", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _preparing = false;
            _btnPrepare.Enabled = true;
            RequestScan(10);
        }
    }

    // =====================================================================
    // Upis
    // =====================================================================

    private void OnStartClicked()
    {
        if (_image is null || _preparing)
        {
            MessageBox.Show(this, "Prvo pripremi image.", Text);
            return;
        }
        SaveUiToSettings();

        if (_cmbMode.SelectedIndex == 1)
        {
            if (_armed) Disarm();
            else
            {
                _armed = true;
                Log("Automatski režim UKLJUČEN — svaki utaknut stik se odmah upisuje.");
                UpdateStartButton();
                RequestScan(10);
            }
            return;
        }

        var candidates = _slots.Values
            .Where(s => s.Present && !s.Running && s.Disk is not null &&
                        s.Phase is SlotPhase.Ready or SlotPhase.Failed or SlotPhase.Cancelled)
            .ToList();
        var withData = _slots.Values.Where(s => s.Present && !s.Running && s.Phase == SlotPhase.HasData).ToList();

        if (withData.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"{withData.Count} stik(ova) ima podatke (slot {string.Join(", ", withData.Select(s => s.Number))}).\n\n" +
                "Da — prebriši i njih\nNe — samo prazni stikovi\nOtkaži — ništa",
                "Stikovi sa podacima", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) candidates.AddRange(withData);
        }

        if (candidates.Count == 0)
        {
            Log("Nema stikova spremnih za upis.");
            return;
        }
        Log($"Start: {candidates.Count} stik(ova).");
        foreach (var s in candidates.OrderBy(s => s.Number)) StartJob(s);
    }

    private void StartAuto()
    {
        if (!_armed || _image is null || _preparing) return;
        foreach (var s in _slots.Values.OrderBy(s => s.Number))
        {
            if (s.Present && !s.Running && s.Phase == SlotPhase.Ready && s.Disk is not null &&
                s.DoneInstanceId != s.Disk.InstanceId)
                StartJob(s);
        }
    }

    private void Disarm()
    {
        _armed = false;
        Log("Automatski režim isključen.");
        UpdateStartButton();
    }

    private void StartJob(SlotState slot)
    {
        var disk = slot.Disk;
        var image = _image;
        if (disk is null || image is null || slot.Running) return;

        string? reason = BlockReasonFor(disk);
        if (reason is not null)
        {
            slot.BlockReason = reason;
            slot.Phase = SlotPhase.Blocked;
            Log($"Slot {slot.Number}: preskočen — {reason}.");
            return;
        }

        var opt = new JobOptions(_chkVerify.Checked, _chkUnique.Checked);
        bool eject = _chkEject.Checked;
        var sem = _parallel;
        var cts = new CancellationTokenSource();

        slot.Running = true;
        slot.Cts = cts;
        slot.LastJobInstanceId = disk.InstanceId;
        slot.Message = "";
        slot.JobStartUtc = DateTime.UtcNow;
        slot.BeginPhase(SlotPhase.Waiting, 0);
        Log($"Slot {slot.Number}: start — disk {disk.DiskNumber}, {disk.Model}, {disk.Size / 1e9:0.0} GB.");

        _ = Task.Run(async () =>
        {
            JobResult? result = null;
            Exception? error = null;
            bool cancelled = false, acquired = false;
            try
            {
                await sem.WaitAsync(cts.Token);
                acquired = true;
                result = WriteJob.Run(disk, image, opt, slot, cts.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { error = ex; }
            finally { if (acquired) sem.Release(); }

            string? ejectError = null;
            if (result is not null && eject)
            {
                await Task.Delay(500);
                ejectError = DiskEnumerator.Eject(disk);
            }
            BeginInvoke(() => FinishJob(slot, disk, image, result, error, cancelled, ejectError));
        });
    }

    private void FinishJob(SlotState slot, DiskInfo disk, ImageInfo image, JobResult? result, Exception? error, bool cancelled, string? ejectError)
    {
        slot.Running = false;
        slot.Cts?.Dispose();
        slot.Cts = null;
        slot.LastDuration = DateTime.UtcNow - slot.JobStartUtc;

        if (result is not null)
        {
            slot.Phase = SlotPhase.Done;
            slot.DoneInstanceId = disk.InstanceId;
            slot.Message = $"Upis {result.WriteTime.TotalSeconds:0}s ({result.WriteMBps:0.0} MB/s)" +
                           (result.VerifyTime > TimeSpan.Zero ? $", provera {result.VerifyTime.TotalSeconds:0}s" : ", bez provere");
            _okCount++;
            Log($"Slot {slot.Number}: OK — {slot.Message}.");
            if (ejectError is not null) Log($"Slot {slot.Number}: {ejectError}");
            _stationLog.Record(slot, disk, image, "OK", slot.Message, result);
        }
        else
        {
            bool removed = !slot.Present;
            slot.Phase = cancelled && !removed ? SlotPhase.Cancelled : SlotPhase.Failed;
            slot.Message = removed ? "Stik je izvađen tokom rada." : cancelled ? "Otkazano." : error?.Message ?? "Nepoznata greška.";
            if (slot.Phase == SlotPhase.Failed) _failCount++;
            Log($"Slot {slot.Number}: {(slot.Phase == SlotPhase.Failed ? "GREŠKA" : "OTKAZANO")} — {slot.Message}");
            _stationLog.Record(slot, disk, image, slot.Phase == SlotPhase.Failed ? "GRESKA" : "OTKAZANO", slot.Message, null);
        }

        if (!_slots.Values.Any(s => s.Running) && _parallel.CurrentCount != (int)_numParallel.Value)
            _parallel = new SemaphoreSlim((int)_numParallel.Value);

        RequestScan(500);
    }

    private void StopAll()
    {
        if (_armed) Disarm();
        int n = 0;
        foreach (var s in _slots.Values.Where(s => s.Running)) { s.Cts?.Cancel(); n++; }
        if (n > 0) Log($"Otkazujem {n} posao(a)...");
    }

    private void OnSlotCommand(SlotControl ctl, string command)
    {
        var slot = ctl.State;
        switch (command)
        {
            case "write":
                if (_image is null) { MessageBox.Show(this, "Prvo pripremi image.", Text); return; }
                if (!slot.Present || slot.Disk is null || slot.Running) return;
                if (slot.Disk.HasVolumes &&
                    MessageBox.Show(this, $"Slot {slot.Number} ({slot.Disk.Model}) — sav sadržaj stika biće prebrisan. Nastaviti?",
                        "Potvrda", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                StartJob(slot);
                break;
            case "cancel":
                slot.Cts?.Cancel();
                break;
            case "eject":
                if (slot.Running || slot.Disk is null) return;
                var disk = slot.Disk;
                Task.Run(() =>
                {
                    string? err = DiskEnumerator.Eject(disk);
                    LogFromAnyThread($"Slot {slot.Number}: {err ?? "izbačen."}");
                });
                break;
        }
    }

    private void ResetSlots()
    {
        if (_slots.Values.Any(s => s.Running))
        {
            MessageBox.Show(this, "Ne može dok traje upis.", Text);
            return;
        }
        if (MessageBox.Show(this, "Obrisati mapiranje portova na brojeve slotova? Numeracija kreće ispočetka po redu utakanja.",
                Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _settings.SlotMap.Clear();
        _settings.Save();
        _slots.Clear();
        foreach (var c in _slotControls.Values) c.Dispose();
        _slotControls.Clear();
        _slotPanel.Controls.Clear();
        RequestScan(10);
    }

    // =====================================================================
    // Prikaz i log
    // =====================================================================

    private void RenderSlots()
    {
        foreach (var c in _slotControls.Values) c.Render();
        int running = _slots.Values.Count(s => s.Running);
        int present = _slots.Values.Count(s => s.Present);
        _lblSummary.Text = $"Utaknuto: {present}   U radu: {running}   OK: {_okCount}   Greške: {_failCount}" +
                           (_armed ? "   ● AUTOMATSKI" : "");
    }

    private void Log(string message)
    {
        _logBox.Items.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        if (_logBox.Items.Count > 5000) _logBox.Items.RemoveAt(0);
        _logBox.TopIndex = Math.Max(0, _logBox.Items.Count - 1);
    }

    private void LogFromAnyThread(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(() => Log(message));
        else Log(message);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_slots.Values.Any(s => s.Running) &&
            MessageBox.Show(this, "Upis je u toku. Prekinuti i zatvoriti? Stikovi u radu ostaće nepotpuni.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        StopAll();
        SaveUiToSettings();
        if (_devNotify != IntPtr.Zero) Win32.UnregisterDeviceNotification(_devNotify);
        base.OnFormClosing(e);
    }
}
