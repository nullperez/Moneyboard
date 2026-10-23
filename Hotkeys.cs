using System.Runtime.InteropServices;

namespace Moneyboard;

/// <summary>System-wide hotkeys via RegisterHotKey; they fire even when the app isn't focused.</summary>
static class Hotkeys
{
    public const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool Register(IntPtr hwnd, int id, Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if (key == Keys.None) return false;
        uint mods = MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Alt)) mods |= MOD_ALT;
        if (keys.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
        if (keys.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;
        return RegisterHotKey(hwnd, id, mods, (uint)key);
    }

    public static void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);

    public static bool IsModifierKey(Keys key) =>
        key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    public static string Describe(Keys keys)
    {
        if ((keys & Keys.KeyCode) == Keys.None) return "";
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(KeyName(keys & Keys.KeyCode));
        return string.Join(" + ", parts);
    }

    static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (k - Keys.NumPad0),
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.Oem6 => "]",
        Keys.Oem5 => "\\",
        Keys.Oem1 => ";",
        Keys.Oem7 => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.Multiply => "Num *",
        Keys.Add => "Num +",
        Keys.Subtract => "Num -",
        Keys.Divide => "Num /",
        Keys.Decimal => "Num .",
        _ => k.ToString(),
    };
}
