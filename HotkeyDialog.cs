namespace Moneyboard;

/// <summary>Captures a key combination. Press the keys, then click OK.</summary>
sealed class HotkeyDialog : Form
{
    public Keys Result { get; private set; }

    readonly Label _display;

    public HotkeyDialog(string title, Keys current)
    {
        Result = current;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 150);
        KeyPreview = true;

        var hint = new Label
        {
            Text = "Press a key combination (e.g. Ctrl + Alt + 1, F13, Num 5).\nTip: use modifiers so the key still works normally elsewhere.",
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0),
        };
        _display = new Label
        {
            Text = Describe(current), Dock = DockStyle.Top, Height = 44,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80, TabStop = false };
        var clear = new Button { Text = "Clear", Width = 80, TabStop = false };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, TabStop = false };
        clear.Click += (_, _) => { Result = Keys.None; _display.Text = Describe(Keys.None); };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(4),
        };
        buttons.Controls.AddRange([cancel, ok, clear]);

        Controls.Add(_display);
        Controls.Add(hint);
        Controls.Add(buttons);
    }

    static string Describe(Keys k) => k == Keys.None ? "(none)" : Hotkeys.Describe(k);

    // Intercept every key (including Tab, arrows, Enter) so any key can be bound.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (msg.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var key = keyData & Keys.KeyCode;
            if (Hotkeys.IsModifierKey(key))
            {
                var mods = keyData & Keys.Modifiers;
                var parts = new List<string>();
                if (mods.HasFlag(Keys.Control)) parts.Add("Ctrl");
                if (mods.HasFlag(Keys.Alt)) parts.Add("Alt");
                if (mods.HasFlag(Keys.Shift)) parts.Add("Shift");
                _display.Text = parts.Count == 0 ? Describe(Result) : string.Join(" + ", parts) + " + …";
            }
            else
            {
                Result = keyData;
                _display.Text = Describe(Result);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
