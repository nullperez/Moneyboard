using NAudio.CoreAudioApi;

namespace Moneyboard;

sealed class MainForm : Form
{
    const int StopHotkeyId = 0x7000; // sound hotkeys use ids 1..N

    readonly AppConfig _config = AppConfig.Load();
    readonly AudioEngine _engine = new();

    readonly ComboBox _outputBox = NewCombo();
    readonly ComboBox _monitorBox = NewCombo();
    readonly ComboBox _micBox = NewCombo();
    readonly TrackBar _soundVol = NewSlider();
    readonly TrackBar _micVol = NewSlider();
    readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, AllowDrop = true,
    };
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _stopHotkeyButton = new() { AutoSize = true };
    bool _loading;

    record DeviceItem(string? Id, string Name) { public override string ToString() => Name; }

    public MainForm()
    {
        Text = "Moneyboard";
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterScreen;

        // --- Devices ---
        var devices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(devices, "Virtual mic output:", _outputBox);
        AddRow(devices, "Monitor (hear it yourself):", _monitorBox);
        AddRow(devices, "Mix in my real mic:", _micBox);
        AddRow(devices, "Sound volume:", _soundVol);
        AddRow(devices, "Mic volume:", _micVol);
        var deviceGroup = new GroupBox { Text = "Audio", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        deviceGroup.Controls.Add(devices);

        // --- Sound list ---
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("Hotkey", 150);
        _list.Columns.Add("File", 380);
        _list.DoubleClick += (_, _) => PlaySelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); if (e.KeyCode == Keys.Enter) PlaySelected(); };
        _list.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _list.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files); };

        // --- Buttons ---
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.Add(NewButton("Add sound…", (_, _) => BrowseForSounds()));
        buttons.Controls.Add(NewButton("Set hotkey…", (_, _) => SetHotkeyForSelected()));
        buttons.Controls.Add(NewButton("Rename…", (_, _) => RenameSelected()));
        buttons.Controls.Add(NewButton("Remove", (_, _) => RemoveSelected()));
        buttons.Controls.Add(NewButton("▶ Play", (_, _) => PlaySelected()));
        buttons.Controls.Add(NewButton("■ Stop all", (_, _) => _engine.StopAll()));
        _stopHotkeyButton.Click += (_, _) => SetStopHotkey();
        buttons.Controls.Add(_stopHotkeyButton);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(deviceGroup);
        Controls.Add(statusStrip);

        _outputBox.SelectedIndexChanged += (_, _) => OnDeviceChanged();
        _monitorBox.SelectedIndexChanged += (_, _) => OnDeviceChanged();
        _micBox.SelectedIndexChanged += (_, _) => OnDeviceChanged();
        _soundVol.ValueChanged += (_, _) => { _config.SoundVolume = _soundVol.Value; _engine.SoundVolume = _soundVol.Value / 100f; Save(); };
        _micVol.ValueChanged += (_, _) => { _config.MicVolume = _micVol.Value; _engine.MicVolume = _micVol.Value / 100f; Save(); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _loading = true;
        _soundVol.Value = Math.Clamp(_config.SoundVolume, 0, 100);
        _micVol.Value = Math.Clamp(_config.MicVolume, 0, 100);
        _engine.SoundVolume = _soundVol.Value / 100f;
        _engine.MicVolume = _micVol.Value / 100f;
        PopulateDevices();
        _loading = false;
        ApplyDevices();
        RefreshList();
        RegisterAllHotkeys();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterAllHotkeys();
        _engine.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Hotkeys.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == StopHotkeyId) _engine.StopAll();
            else if (id >= 1 && id <= _config.Sounds.Count) Play(_config.Sounds[id - 1]);
        }
        base.WndProc(ref m);
    }

    // ---------- Devices ----------

    void PopulateDevices()
    {
        var render = AudioEngine.RenderDevices().Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();
        var capture = AudioEngine.CaptureDevices().Select(d => new DeviceItem(d.ID, d.FriendlyName)).ToList();
        var none = new DeviceItem(null, "(none)");

        // No output is useless, so keep auto-detecting VB-CABLE until one is chosen.
        Fill(_outputBox, [none, .. render], string.IsNullOrEmpty(_config.OutputDeviceId)
            ? render.FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))?.Id
            : _config.OutputDeviceId);
        Fill(_monitorBox, [none, .. render], _config.MonitorDeviceId ?? DefaultId(DataFlow.Render));
        Fill(_micBox, [none, .. capture], _config.MicDeviceId);
    }

    static string? DefaultId(DataFlow flow)
    {
        try { using var e = new MMDeviceEnumerator(); return e.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; }
        catch { return null; }
    }

    static void Fill(ComboBox box, List<DeviceItem> items, string? selectId)
    {
        box.Items.Clear();
        foreach (var i in items) box.Items.Add(i);
        box.SelectedIndex = Math.Max(0, items.FindIndex(i => (i.Id ?? "") == (selectId ?? "")));
    }

    void OnDeviceChanged()
    {
        if (_loading) return;
        ApplyDevices();
    }

    void ApplyDevices()
    {
        // "" = explicitly none (null means "never chosen, use a default").
        _config.OutputDeviceId = (_outputBox.SelectedItem as DeviceItem)?.Id ?? "";
        _config.MonitorDeviceId = (_monitorBox.SelectedItem as DeviceItem)?.Id ?? "";
        _config.MicDeviceId = (_micBox.SelectedItem as DeviceItem)?.Id ?? "";
        Save();

        var errors = _engine.Configure(_config.OutputDeviceId, _config.MonitorDeviceId, _config.MicDeviceId);
        if (errors.Count > 0)
            SetStatus("⚠ " + string.Join("  |  ", errors));
        else if (_config.OutputDeviceId == "")
            SetStatus(_outputBox.Items.Cast<DeviceItem>().Any(d => d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                ? "Pick \"CABLE Input\" as the virtual mic output."
                : "⚠ VB-CABLE not found. Install it from vb-audio.com/Cable, then restart Moneyboard.");
        else
            SetStatus("Ready. In Discord/OBS/etc. choose \"CABLE Output\" as your microphone.");
    }

    // ---------- Sounds ----------

    void BrowseForSounds()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose sound files",
            Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.wma|MP3 files|*.mp3|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
    }

    void AddFiles(IEnumerable<string> files)
    {
        foreach (var f in files.Where(File.Exists))
            _config.Sounds.Add(new SoundEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f });
        Save();
        RefreshList();
        RegisterAllHotkeys();
        if (_list.Items.Count > 0) { _list.SelectedIndices.Clear(); _list.Items[^1].Selected = true; }
    }

    SoundEntry? Selected => _list.SelectedIndices.Count == 1 ? _config.Sounds[_list.SelectedIndices[0]] : null;

    void RemoveSelected()
    {
        if (Selected is not { } s) return;
        _config.Sounds.Remove(s);
        Save();
        RefreshList();
        RegisterAllHotkeys();
    }

    void RenameSelected()
    {
        if (Selected is not { } s) return;
        var name = Prompt("Rename sound", "Name:", s.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        s.Name = name.Trim();
        Save();
        RefreshList();
    }

    void SetHotkeyForSelected()
    {
        if (Selected is not { } s) { SetStatus("Select a sound first."); return; }
        var keys = CaptureHotkey($"Hotkey for \"{s.Name}\"", s.Hotkey);
        if (keys is null) return;
        // A combo can only belong to one action.
        foreach (var other in _config.Sounds.Where(o => o != s && o.Hotkey == keys)) other.Hotkey = Keys.None;
        if (_config.StopHotkey == keys) _config.StopHotkey = Keys.None;
        s.Hotkey = keys.Value;
        Save();
        RefreshList();
        RegisterAllHotkeys();
    }

    void SetStopHotkey()
    {
        var keys = CaptureHotkey("Hotkey for Stop all", _config.StopHotkey);
        if (keys is null) return;
        foreach (var o in _config.Sounds.Where(o => o.Hotkey == keys)) o.Hotkey = Keys.None;
        _config.StopHotkey = keys.Value;
        Save();
        RefreshList();
        RegisterAllHotkeys();
    }

    Keys? CaptureHotkey(string title, Keys current)
    {
        // Our own registered hotkeys would swallow the keypresses, so release them while capturing.
        UnregisterAllHotkeys();
        try
        {
            using var dlg = new HotkeyDialog(title, current);
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Result : null;
        }
        finally { RegisterAllHotkeys(); }
    }

    void PlaySelected() { if (Selected is { } s) Play(s); }

    void Play(SoundEntry s)
    {
        try
        {
            _engine.Play(s.Path);
            SetStatus($"Playing {s.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"⚠ Couldn't play {s.Name}: {ex.Message}");
        }
    }

    // ---------- Hotkeys ----------

    readonly HashSet<int> _failedIds = new();

    void RegisterAllHotkeys()
    {
        UnregisterAllHotkeys();
        _failedIds.Clear();
        for (int i = 0; i < _config.Sounds.Count; i++) TryRegister(i + 1, _config.Sounds[i].Hotkey);
        TryRegister(StopHotkeyId, _config.StopHotkey);
        RefreshHotkeyLabels();
    }

    readonly HashSet<int> _registeredIds = new();

    void UnregisterAllHotkeys()
    {
        foreach (var id in _registeredIds) Hotkeys.Unregister(Handle, id);
        _registeredIds.Clear();
    }

    void TryRegister(int id, Keys k)
    {
        if (k == Keys.None) return;
        if (Hotkeys.Register(Handle, id, k)) _registeredIds.Add(id);
        else _failedIds.Add(id);
    }

    // ---------- UI helpers ----------

    void RefreshList()
    {
        int sel = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in _config.Sounds)
            _list.Items.Add(new ListViewItem([s.Name, "", s.Path]));
        _list.EndUpdate();
        if (sel >= 0 && sel < _list.Items.Count) _list.Items[sel].Selected = true;
        RefreshHotkeyLabels();
    }

    void RefreshHotkeyLabels()
    {
        for (int i = 0; i < _list.Items.Count && i < _config.Sounds.Count; i++)
        {
            var k = _config.Sounds[i].Hotkey;
            _list.Items[i].SubItems[1].Text = k == Keys.None ? "" :
                Hotkeys.Describe(k) + (_failedIds.Contains(i + 1) ? "  (in use by another app)" : "");
        }
        _stopHotkeyButton.Text = "Stop-all hotkey: " + (_config.StopHotkey == Keys.None ? "(none)" :
            Hotkeys.Describe(_config.StopHotkey) + (_failedIds.Contains(StopHotkeyId) ? " (in use)" : ""));
    }

    void SetStatus(string text) => _status.Text = text;

    void Save()
    {
        if (_loading) return;
        try { _config.Save(); } catch (Exception ex) { SetStatus("⚠ Couldn't save settings: " + ex.Message); }
    }

    static ComboBox NewCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    static TrackBar NewSlider() => new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Dock = DockStyle.Fill, AutoSize = false, Height = 30 };

    static Button NewButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    static void AddRow(TableLayoutPanel t, string label, Control c)
    {
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) });
        t.Controls.Add(c);
    }

    string? Prompt(string title, string label, string value)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 100),
        };
        var lbl = new Label { Text = label, Location = new Point(10, 12), AutoSize = true };
        var box = new TextBox { Text = value, Location = new Point(10, 34), Width = 340 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(194, 66) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(275, 66) };
        f.Controls.AddRange([lbl, box, ok, cancel]);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }
}
