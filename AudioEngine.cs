using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Moneyboard;

/// <summary>
/// Routes sounds to the virtual cable (what other apps hear as a microphone) and optionally to a
/// monitor device so you can hear them too. Optionally mixes your real mic into the virtual cable,
/// so apps using the virtual mic still hear your voice.
/// </summary>
sealed class AudioEngine : IDisposable
{
    OutputBus? _virtual;
    OutputBus? _monitor;
    MicPassthrough? _mic;
    float _soundVolume = 0.8f;
    float _micVolume = 1f;

    public static List<MMDevice> RenderDevices() => Devices(DataFlow.Render);
    public static List<MMDevice> CaptureDevices() => Devices(DataFlow.Capture);

    static List<MMDevice> Devices(DataFlow flow)
    {
        using var e = new MMDeviceEnumerator();
        return e.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
    }

    /// <summary>(Re)opens all devices. Each id may be null to disable that route. Returns error messages.</summary>
    public List<string> Configure(string? outputId, string? monitorId, string? micId)
    {
        DisposeDevices();
        var errors = new List<string>();
        using var e = new MMDeviceEnumerator();

        MMDevice? Get(string? id, string what)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return e.GetDevice(id); }
            catch (Exception ex) { errors.Add($"{what}: {ex.Message}"); return null; }
        }

        var outDev = Get(outputId, "Virtual output");
        var monDev = Get(monitorId, "Monitor");
        var micDev = Get(micId, "Mic");

        try { if (outDev != null) _virtual = new OutputBus(outDev, _soundVolume); }
        catch (Exception ex) { errors.Add($"Virtual output: {ex.Message}"); }

        // Don't double-play if monitor is the same device as the virtual output.
        if (monDev != null && monDev.ID != outDev?.ID)
        {
            try { _monitor = new OutputBus(monDev, _soundVolume); }
            catch (Exception ex) { errors.Add($"Monitor: {ex.Message}"); }
        }

        if (micDev != null && _virtual != null)
        {
            try { _mic = new MicPassthrough(micDev, _virtual, _micVolume); }
            catch (Exception ex) { errors.Add($"Mic passthrough: {ex.Message}"); }
        }
        return errors;
    }

    public float SoundVolume
    {
        get => _soundVolume;
        set { _soundVolume = value; _virtual?.SetSoundVolume(value); _monitor?.SetSoundVolume(value); }
    }

    public float MicVolume
    {
        get => _micVolume;
        set { _micVolume = value; _mic?.SetVolume(value); }
    }

    public void Play(string path)
    {
        _virtual?.Play(path);
        _monitor?.Play(path);
    }

    public void StopAll()
    {
        _virtual?.StopAll();
        _monitor?.StopAll();
    }

    void DisposeDevices()
    {
        _mic?.Dispose(); _mic = null;
        _virtual?.Dispose(); _virtual = null;
        _monitor?.Dispose(); _monitor = null;
    }

    public void Dispose() => DisposeDevices();

    /// <summary>Converts any sample provider to stereo float at the given rate.</summary>
    internal static ISampleProvider Adapt(ISampleProvider source, WaveFormat target)
    {
        ISampleProvider s = source;
        if (s.WaveFormat.Channels != 2) s = new ToStereo(s);
        if (s.WaveFormat.SampleRate != target.SampleRate) s = new WdlResamplingSampleProvider(s, target.SampleRate);
        return s;
    }
}

/// <summary>One playback device: sound mixer → volume → top mixer (+ mic) → WASAPI.</summary>
sealed class OutputBus : IDisposable
{
    public WaveFormat Format { get; }
    public MixingSampleProvider Top { get; }
    readonly MixingSampleProvider _sounds;
    readonly VolumeSampleProvider _soundVol;
    readonly WasapiOut _out;
    readonly List<ActiveSound> _active = new();

    public OutputBus(MMDevice device, float volume)
    {
        Format = WaveFormat.CreateIeeeFloatWaveFormat(device.AudioClient.MixFormat.SampleRate, 2);
        _sounds = new MixingSampleProvider(Format) { ReadFully = true };
        _soundVol = new VolumeSampleProvider(_sounds) { Volume = volume };
        Top = new MixingSampleProvider(Format) { ReadFully = true };
        Top.AddMixerInput(_soundVol);
        _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
        _out.Init(Top);
        _out.Play();
    }

    public void SetSoundVolume(float v) => _soundVol.Volume = v;

    public void Play(string path)
    {
        var sound = new ActiveSound(path, Format, Remove);
        lock (_active) _active.Add(sound);
        _sounds.AddMixerInput(sound);
    }

    void Remove(ActiveSound s) { lock (_active) _active.Remove(s); }

    public void StopAll()
    {
        lock (_active) foreach (var s in _active) s.RequestStop();
    }

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
        // Audio thread is stopped now, so it's safe to close any still-open files.
        ActiveSound[] remaining;
        lock (_active) { remaining = _active.ToArray(); _active.Clear(); }
        foreach (var s in remaining) s.Dispose();
    }
}

/// <summary>A playing file. Stops are applied on the audio thread so the reader is never disposed mid-read.</summary>
sealed class ActiveSound : ISampleProvider, IDisposable
{
    readonly AudioFileReader _reader;
    readonly ISampleProvider _chain;
    readonly Action<ActiveSound> _onEnd;
    volatile bool _stopRequested;
    int _disposed;

    public ActiveSound(string path, WaveFormat target, Action<ActiveSound> onEnd)
    {
        _reader = new AudioFileReader(path);
        _chain = AudioEngine.Adapt(_reader, target);
        _onEnd = onEnd;
    }

    public WaveFormat WaveFormat => _chain.WaveFormat;

    public void RequestStop() => _stopRequested = true;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_stopRequested || _disposed != 0) { Dispose(); return 0; }
        int n = _chain.Read(buffer, offset, count);
        if (n < count) Dispose(); // mixer drops inputs that return short reads
        return n;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _reader.Dispose();
        _onEnd(this);
    }
}

/// <summary>Captures a real microphone and feeds it into a bus's top mixer.</summary>
sealed class MicPassthrough : IDisposable
{
    readonly WasapiCapture _capture;
    readonly BufferedWaveProvider _buffer;
    readonly VolumeSampleProvider _vol;
    readonly OutputBus _bus;
    readonly int _maxBufferedBytes;

    public MicPassthrough(MMDevice device, OutputBus bus, float volume)
    {
        _bus = bus;
        _capture = new WasapiCapture(device, true, 20);
        var fmt = Simplify(_capture.WaveFormat);
        _buffer = new BufferedWaveProvider(fmt)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
            BufferDuration = TimeSpan.FromMilliseconds(500),
        };
        _maxBufferedBytes = fmt.AverageBytesPerSecond / 10; // cap latency drift at ~100 ms
        _capture.DataAvailable += (_, e) =>
        {
            if (_buffer.BufferedBytes > _maxBufferedBytes) _buffer.ClearBuffer();
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };
        _vol = new VolumeSampleProvider(AudioEngine.Adapt(_buffer.ToSampleProvider(), bus.Format)) { Volume = volume };
        bus.Top.AddMixerInput(_vol);
        _capture.StartRecording();
    }

    public void SetVolume(float v) => _vol.Volume = v;

    static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    // WASAPI reports WAVE_FORMAT_EXTENSIBLE; convert to an equivalent plain format NAudio converters accept.
    static WaveFormat Simplify(WaveFormat f)
    {
        if (f is WaveFormatExtensible ext)
        {
            if (ext.SubFormat == FloatSubtype)
                return WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels);
            if (ext.SubFormat == PcmSubtype)
                return new WaveFormat(f.SampleRate, f.BitsPerSample, f.Channels);
        }
        return f;
    }

    public void Dispose()
    {
        _bus.Top.RemoveMixerInput(_vol);
        _capture.StopRecording();
        _capture.Dispose();
    }
}

/// <summary>Mono → duplicated stereo; multichannel → first two channels.</summary>
sealed class ToStereo : ISampleProvider
{
    readonly ISampleProvider _src;
    readonly int _ch;
    float[] _buf = [];

    public ToStereo(ISampleProvider src)
    {
        _src = src;
        _ch = src.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        int need = frames * _ch;
        if (_buf.Length < need) _buf = new float[need];
        int got = _src.Read(_buf, 0, need) / _ch;
        for (int i = 0; i < got; i++)
        {
            float l = _buf[i * _ch];
            float r = _ch == 1 ? l : _buf[i * _ch + 1];
            buffer[offset + i * 2] = l;
            buffer[offset + i * 2 + 1] = r;
        }
        return got * 2;
    }
}
