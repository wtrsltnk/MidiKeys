namespace MidiKeys.Services;

public enum StepMode
{
    Play,
    Skip,   // step is passed over
    Reset,  // reaching it jumps straight back to the first step
}

public sealed class SequencerStep
{
    /// <summary>0–24 semitones above the base note (quantised two-octave range).</summary>
    public int Pitch { get; set; } = 12;

    /// <summary>Gate bus 1: the step plays a note.</summary>
    public bool Gate { get; set; } = true;

    /// <summary>Gate bus 2: play the note at accent velocity.</summary>
    public bool Accent { get; set; }

    /// <summary>Gate bus 3: hold the note into the next step (same pitch = tie).</summary>
    public bool Legato { get; set; }

    public StepMode Mode { get; set; }
}

/// <summary>
/// 16-step sequencer after the ARP / Behringer 1601 on one MIDI channel: one 16-step sequence or
/// two independent 8-step sequences (two voices on that channel), three gate buses, skip/reset per step and
/// an optional random order. Steps are sixteenth notes on the shared <see cref="MusicClock"/>.
/// </summary>
public sealed class StepSequencer : IClockClient, IDisposable
{
    public const int StepCount = 16;
    public const int PitchRange = 24;
    const int NormalVelocity = 96;
    const int AccentVelocity = 127;
    const double LegatoOverlapMs = 5; // note-off lands just after the next note-on

    readonly MusicClock _clock;
    readonly SoundingNotes _sounding;
    readonly object _lock = new();
    readonly Random _random = new();
    readonly int[] _positions = [-1, -1];          // current step per sequence (A, B)

    volatile bool _running;
    double _gateLength = 0.5;

    public SequencerStep[] Steps { get; } = Enumerable.Range(0, StepCount).Select(_ => new SequencerStep()).ToArray();

    /// <summary>Gate length as a fraction of a step (staccato … nearly legato).</summary>
    public double GateLength { get => _gateLength; set => _gateLength = Math.Clamp(value, 0.05, 0.95); }

    /// <summary>Two 8-step sequences (A = steps 1–8, B = steps 9–16) instead of one of 16.</summary>
    public bool Split { get; set; }

    public bool RandomOrder { get; set; }

    /// <summary>MIDI note of a slider at the bottom.</summary>
    public int BaseNote { get; set; } = 48;

    /// <summary>MIDI channel (0–15) this sequencer plays on; settings are stored per channel.</summary>
    public int Channel { get; }

    public bool IsRunning => _running;

    /// <summary>Raised on the clock thread after every step: current step per sequence (-1 = none).</summary>
    public event Action<int[]>? StepChanged;

    /// <summary>Raised on the thread that called Start/Stop.</summary>
    public event EventHandler? RunningChanged;

    public StepSequencer(IMidiOutput midi, MusicClock clock, int channel)
    {
        Channel = channel;
        _clock = clock;
        _sounding = new SoundingNotes(midi);
        Load();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _clock.Join(this);
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _clock.Leave(this);
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the first step: immediately when stopped, on the next step when running.</summary>
    public void Reset()
    {
        int[] snapshot;
        lock (_lock)
        {
            _positions[0] = _positions[1] = -1;
            snapshot = (int[])_positions.Clone();
        }
        if (!_running)
            StepChanged?.Invoke(snapshot);
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- timing (clock thread)

    const int TicksPerStep = MusicClock.Ppqn / 4; // a step is a sixteenth note

    void IClockClient.ReleaseDue(double now) => _sounding.ReleaseDue(now);
    double IClockClient.NextRelease => _sounding.NextRelease;
    void IClockClient.ReleaseAll() => _sounding.ReleaseAll();

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        if (tick % TicksPerStep != 0) return;
        double stepMs = tickMs * TicksPerStep;

        int[] snapshot;
        lock (_lock)
        {
            if (Split)
            {
                PlayLane(0, 0, 8, Channel, now, stepMs);
                PlayLane(1, 8, 8, Channel, now, stepMs);
            }
            else
            {
                PlayLane(0, 0, StepCount, Channel, now, stepMs);
                _positions[1] = -1;
            }
            snapshot = (int[])_positions.Clone();
        }
        StepChanged?.Invoke(snapshot);
    }

    void PlayLane(int lane, int start, int length, int channel, double now, double stepMs)
    {
        int pos = NextPosition(_positions[lane], start, length);
        _positions[lane] = pos;
        if (pos < 0) return;

        var step = Steps[pos];
        if (!step.Gate) return;

        int note = Math.Clamp(BaseNote + step.Pitch, 0, 127);
        double offAt = now + (step.Legato ? stepMs + LegatoOverlapMs : Math.Max(5, stepMs * _gateLength));

        if (!_sounding.TryTie(channel, note, offAt, step.Legato))
            _sounding.Play(channel, note, step.Accent ? AccentVelocity : NormalVelocity, offAt, step.Legato);
    }

    int NextPosition(int pos, int start, int length)
    {
        for (int tries = 0; tries < length * 2; tries++)
        {
            if (RandomOrder)
                pos = start + _random.Next(length);
            else
                pos = pos < start || pos >= start + length - 1 ? start : pos + 1;

            switch (Steps[pos].Mode)
            {
                case StepMode.Play:
                    return pos;
                case StepMode.Reset when !RandomOrder:
                    pos = -1; // back to the first step, within this same tick
                    break;
            }
        }
        return -1; // every step is skipped
    }

    // ---------------------------------------------------------------- persistence

    static readonly string[] Keys = ["steps", "gate", "split", "random", "base"];

    string Key(string name) => $"seq{Channel}.{name}";

    /// <summary>Removes the stored settings (the sequencer is deleted).</summary>
    public void Forget()
    {
        foreach (var name in Keys)
            Preferences.Default.Remove(Key(name));
    }

    public void Save()
    {
        var p = Preferences.Default;
        p.Set(Key("steps"), string.Join(';', Steps.Select(s =>
            $"{s.Pitch}:{(s.Gate ? "g" : "")}{(s.Accent ? "a" : "")}{(s.Legato ? "l" : "")}:{(int)s.Mode}")));
        p.Set(Key("gate"), GateLength);
        p.Set(Key("split"), Split);
        p.Set(Key("random"), RandomOrder);
        p.Set(Key("base"), BaseNote);
    }

    void Load()
    {
        var p = Preferences.Default;
        GateLength = p.Get(Key("gate"), GateLength);
        Split = p.Get(Key("split"), Split);
        RandomOrder = p.Get(Key("random"), RandomOrder);
        BaseNote = p.Get(Key("base"), BaseNote);

        var parts = p.Get(Key("steps"), "").Split(';');
        for (int i = 0; i < Math.Min(parts.Length, StepCount); i++)
        {
            var f = parts[i].Split(':');
            if (f.Length != 3 || !int.TryParse(f[0], out int pitch) || !int.TryParse(f[2], out int mode))
                continue;
            var s = Steps[i];
            s.Pitch = Math.Clamp(pitch, 0, PitchRange);
            s.Gate = f[1].Contains('g');
            s.Accent = f[1].Contains('a');
            s.Legato = f[1].Contains('l');
            s.Mode = Enum.IsDefined((StepMode)mode) ? (StepMode)mode : StepMode.Play;
        }
    }
}
