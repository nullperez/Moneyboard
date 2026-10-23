# Moneyboard

A Windows soundboard: pick MP3s, bind global hotkeys, and play them into a virtual microphone.

## One-time setup: virtual microphone

Windows can't create a virtual mic without a driver, so Moneyboard uses the free **VB-Audio Virtual Cable**:

1. Download from https://vb-audio.com/Cable/, extract, run `VBCABLE_Setup_x64.exe` as administrator, reboot.
2. That adds two devices:
   - **CABLE Input** (playback): Moneyboard plays into this.
   - **CABLE Output** (recording): select this as your microphone in Discord, OBS, games, etc.

## Run

```
dotnet run -c Release
```

Or publish a standalone exe: `dotnet publish -c Release -r win-x64 --self-contained false -o publish`

## Use

- **Virtual mic output**: `CABLE Input` (picked automatically if it's installed).
- **Monitor**: your headphones, so you hear the sounds too. Set it to `(none)` to turn it off.
- **Mix in my real mic**: pick your real microphone (e.g. QuadCast). Your voice then goes through the
  virtual mic along with the sounds, so people can still hear you.
- **Add sound…** (or drag MP3s onto the list) → select one → **Set hotkey…** → press a combo → OK.
- Hotkeys work system-wide, even when Moneyboard is minimized or a game is focused.
- **Stop-all hotkey** cuts every playing sound.
- Double-click or press Enter to preview a sound; Delete removes it.

Tip: a hotkey grabs its key combination system-wide, so use modifier combos (Ctrl+Alt+1), numpad
keys, or F13–F24 rather than plain letters.

Settings are saved to `%APPDATA%\Moneyboard\config.json`.
