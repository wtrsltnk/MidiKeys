namespace MidiKeys.Services;

/// <summary>
/// The round 8-step sequencer of a Dato DUO on one MIDI channel. Every step plays a note of a
/// two-octave pentatonic scale, so whatever you enter sounds good. The root moves the keyboard by
/// octaves, random plays the steps in random order, boost plays every note with full velocity.
/// Runs on the shared <see cref="MusicClock"/>.
/// </summary>
public sealed class DuoSequencer : IClockClient, IInstrument
{
    public const int StepCount = 8;
    public const int KeyCount = 10;                 // two octaves of the pentatonic scale
    static readonly int[] Scale = [0, 2, 4, 7, 9];  // major pentatonic
    public static readonly int[] Rates = [MusicClock.Ppqn, MusicClock.Ppqn / 2, MusicClock.Ppqn / 4]; // 1/4, 1/8, 1/16
    const int NormalVelocity = 96;
    const int BoostVelocity = 127;

    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly SoundingNotes _sounding;
    readonly object _lock = new();
    readonly Random _random = new();

    volatile bool _running;
    volatile int _position = -1;
    double _gate = 0.6;

    /// <summary>Per step the key 0–9 of the scale it plays.</summary>
    public int[] Keys { get; } = [0, 2, 4, 2, 5, 4, 2, 0];

    public bool[] On { get; } = Enumerable.Repeat(true, StepCount).ToArray();

    /// <summary>MIDI note of the lowest key.</summary>
    public int Root { get; set; } = 60;

    /// <summary>Index in <see cref="Rates"/>.</summary>
    public int Rate { get; set; } = 1;

    public bool RandomOrder { get; set; }

    public bool Boost { get; set; }

    /// <summary>Gate length as a fraction of a step.</summary>
    public double Gate { get => _gate; set => _gate = Math.Clamp(value, 0.05, 0.95); }

    public int Channel { get; }

    public int Slot { get; }

    public bool IsRunning => _running;

    /// <summary>The step that plays now, or -1.</summary>
    public int Position => _position;

    /// <summary>Raised on the clock thread at every step.</summary>
    public event Action<int>? StepChanged;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? RunningChanged;

    public DuoSequencer(IMidiOutput midi, MusicClock clock, int channel, int slot)
    {
        _midi = midi;
        _clock = clock;
        _sounding = new SoundingNotes(midi);
        Channel = channel;
        Slot = slot;
        Load();
    }

    /// <summary>The MIDI note of a key of the scale.</summary>
    public int NoteOf(int key) => Math.Clamp(Root + 12 * (key / Scale.Length) + Scale[key % Scale.Length], 0, 127);

    public void Start()
    {
        if (_running) return;
        _position = -1;
        _running = true;
        _clock.Join(this);
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _clock.Leave(this);
        _position = -1;
        RunningChanged?.Invoke(this, EventArgs.Empty);
        StepChanged?.Invoke(-1);
    }

    /// <summary>Back to the first step on the next step.</summary>
    public void Reset() => _position = -1;

    /// <summary>Plays a key by itself while the sequencer is stopped, so you hear what you enter (UI thread).</summary>
    public void Preview(int key)
    {
        int note = NoteOf(key);
        _midi.NoteOn(Channel, note, Boost ? BoostVelocity : NormalVelocity);
        int ms = (int)Math.Max(80, 60000.0 / _clock.Bpm * Rates[Rate] / MusicClock.Ppqn * _gate);
        _ = Task.Delay(ms).ContinueWith(_ => _midi.NoteOff(Channel, note));
    }

    // ---------------------------------------------------------------- timing (clock thread)

    void IClockClient.ReleaseDue(double now) => _sounding.ReleaseDue(now);
    double IClockClient.NextRelease => _sounding.NextRelease;
    void IClockClient.ReleaseAll() => _sounding.ReleaseAll();

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        int ticksPerStep = Rates[Math.Clamp(Rate, 0, Rates.Length - 1)];
        if (tick % ticksPerStep != 0) return;

        int pos;
        lock (_lock)
        {
            pos = RandomOrder ? _random.Next(StepCount) : (_position + 1) % StepCount;
            _position = pos;
            if (On[pos])
                _sounding.Play(Channel, NoteOf(Keys[pos]), Boost ? BoostVelocity : NormalVelocity,
                    now + Math.Max(5, tickMs * ticksPerStep * _gate));
        }
        StepChanged?.Invoke(pos);
    }

    // ---------------------------------------------------------------- persistence

    static readonly string[] Names = ["keys", "on", "root", "rate", "random", "boost", "gate"];

    string Key(string name) => $"duo{Slot}.{name}";

    /// <summary>Removes the stored settings (the sequencer is deleted).</summary>
    public void Forget()
    {
        foreach (var name in Names)
            Preferences.Default.Remove(Key(name));
    }

    public void Save()
    {
        var p = Preferences.Default;
        p.Set(Key("keys"), string.Join(',', Keys));
        p.Set(Key("on"), new string(On.Select(on => on ? '1' : '0').ToArray()));
        p.Set(Key("root"), Root);
        p.Set(Key("rate"), Rate);
        p.Set(Key("random"), RandomOrder);
        p.Set(Key("boost"), Boost);
        p.Set(Key("gate"), Gate);
    }

    void Load()
    {
        var p = Preferences.Default;
        var keys = p.Get(Key("keys"), "").Split(',');
        if (keys.Length == StepCount)
            for (int i = 0; i < StepCount; i++)
                if (int.TryParse(keys[i], out int k))
                    Keys[i] = Math.Clamp(k, 0, KeyCount - 1);
        var on = p.Get(Key("on"), "");
        for (int i = 0; i < Math.Min(on.Length, StepCount); i++)
            On[i] = on[i] != '0';
        Root = Math.Clamp(p.Get(Key("root"), Root), 24, 96);
        Rate = Math.Clamp(p.Get(Key("rate"), Rate), 0, Rates.Length - 1);
        RandomOrder = p.Get(Key("random"), RandomOrder);
        Boost = p.Get(Key("boost"), Boost);
        Gate = p.Get(Key("gate"), Gate);
    }
}
