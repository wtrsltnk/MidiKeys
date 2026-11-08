namespace MidiKeys.Services;

/// <summary>The eight positions of the BQ-10's SEQUENCE MODE switch.</summary>
public enum BqMode
{
    LoopReverseStepped,  // A, then B in reverse; only the STEP button advances
    ContinuousReverse,   // A, then B in reverse, looping
    ThreeChannel,        // A, B and C run side by side, 8 steps each
    SingleStepped,       // A, then B; only the STEP button advances
    Continuous,          // A, then B, looping
    Single,              // A, then B, then it stops
    Alternate,           // A1, B1, A2, B2 …
    Random,              // random steps from A and B
}

/// <summary>What row C controls over MIDI (on the hardware it is a free control voltage).</summary>
public enum BqTarget { Velocity, Cutoff, Resonance, Modulation }

/// <summary>Current position: step 0–7 of A, B and C, or -1 when that row is not playing.</summary>
public readonly record struct BqPosition(int A, int B, int C)
{
    public static readonly BqPosition None = new(-1, -1, -1);
}

/// <summary>
/// 3 × 8-step sequencer after the Behringer BQ-10 (Korg SQ-10) on one MIDI channel. Rows A and B
/// are pitches, row C sets the velocity or a controller per step. The sequence mode decides how
/// A and B are combined; C always runs on, so it repeats twice over a 16-step A+B sequence.
/// Steps are sixteenth notes on the shared <see cref="MusicClock"/>.
/// </summary>
public sealed class BqSequencer : IClockClient, IInstrument
{
    public const int StepCount = 8;
    public static readonly int[] Ranges = [1, 2, 5]; // octaves: the 1 V, 2 V and 5 V positions
    const int TicksPerStep = MusicClock.Ppqn / 4;
    const int PlainVelocity = 100;

    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly SoundingNotes _sounding;
    readonly object _lock = new();
    readonly Random _random = new();

    volatile bool _running;
    int _count = -1;           // steps played since start or reset
    int _sentPortamento = -1;  // last CC 5 value sent
    int _sentControl = -1;     // last row-C controller value sent
    BqPosition _position = BqPosition.None;
    double _duty = 0.5;

    /// <summary>Knob positions 0–1: [row][step], rows A, B, C.</summary>
    public double[][] Knobs { get; } = [Filled(0.5), Filled(0.5), Filled(0.8)];

    /// <summary>Range of rows A and B in octaves (one of <see cref="Ranges"/>).</summary>
    public int[] Range { get; } = [1, 1];

    /// <summary>Portamento of rows A and B, 0–1 (sent as CC 5 / CC 65).</summary>
    public double[] Portamento { get; } = [0, 0];

    public BqMode Mode { get; set; } = BqMode.Continuous;

    public BqTarget Target { get; set; } = BqTarget.Velocity;

    /// <summary>Gate length as a fraction of a step, 10–90 %.</summary>
    public double Duty { get => _duty; set => _duty = Math.Clamp(value, 0.1, 0.9); }

    /// <summary>MIDI note of a knob turned fully left.</summary>
    public int BaseNote { get; set; } = 48;

    public int Channel { get; }

    public int Slot { get; }

    public bool IsRunning => _running;

    /// <summary>The stepped modes only move with the STEP button.</summary>
    public bool IsStepped => Mode is BqMode.LoopReverseStepped or BqMode.SingleStepped;

    /// <summary>Raised on the clock thread (or the UI thread for STEP) after every step.</summary>
    public event Action<BqPosition>? StepChanged;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? RunningChanged;

    public BqSequencer(IMidiOutput midi, MusicClock clock, int channel, int slot)
    {
        _midi = midi;
        _clock = clock;
        _sounding = new SoundingNotes(midi);
        Channel = channel;
        Slot = slot;
        Load();
    }

    static double[] Filled(double value) => Enumerable.Repeat(value, StepCount).ToArray();

    public void Start()
    {
        if (_running || IsStepped) return;
        lock (_lock) _count = -1;
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

    /// <summary>Back to the first step.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _count = -1;
            _position = BqPosition.None;
        }
        if (!_running)
            StepChanged?.Invoke(BqPosition.None);
    }

    /// <summary>The STEP button: when stopped, play the next step by hand (UI thread).</summary>
    public void Step()
    {
        if (_running) return;
        List<(int Channel, int Note)> played = new();
        BqPosition position;
        lock (_lock)
        {
            position = Advance((channel, note, velocity) =>
            {
                _midi.NoteOn(channel, note, velocity);
                played.Add((channel, note));
            });
        }
        StepChanged?.Invoke(position);

        // No clock to end the notes: let them sound for a beat's worth of the duty cycle.
        int ms = (int)Math.Max(80, 60000.0 / _clock.Bpm * _duty);
        _ = Task.Delay(ms).ContinueWith(_ =>
        {
            foreach (var (channel, note) in played)
                _midi.NoteOff(channel, note);
        });
    }

    public BqPosition Position
    {
        get { lock (_lock) return _position; }
    }

    // ---------------------------------------------------------------- timing (clock thread)

    void IClockClient.ReleaseDue(double now) => _sounding.ReleaseDue(now);
    double IClockClient.NextRelease => _sounding.NextRelease;
    void IClockClient.ReleaseAll() => _sounding.ReleaseAll();

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        if (tick % TicksPerStep != 0) return;
        double offAt = now + Math.Max(5, tickMs * TicksPerStep * _duty);

        BqPosition position;
        lock (_lock)
        {
            if (Mode == BqMode.Single && _count >= 2 * StepCount - 1)
            {
                // One pass of A and B is done. Stop on the UI thread: the clock is iterating its clients now.
                MainThread.BeginInvokeOnMainThread(Stop);
                return;
            }
            position = Advance((channel, note, velocity) => _sounding.Play(channel, note, velocity, offAt));
        }
        StepChanged?.Invoke(position);
    }

    /// <summary>Moves on one step and plays it. Caller holds the lock.</summary>
    BqPosition Advance(Action<int, int, int> play)
    {
        int n = ++_count;
        int c = n % StepCount;
        int i = n % (2 * StepCount);
        int a = -1, b = -1;

        switch (Mode)
        {
            case BqMode.ThreeChannel:
                a = b = c;
                break;
            case BqMode.LoopReverseStepped or BqMode.ContinuousReverse:
                if (i < StepCount) a = i; else b = 2 * StepCount - 1 - i;
                break;
            case BqMode.Alternate:
                if (i % 2 == 0) a = i / 2; else b = i / 2;
                break;
            case BqMode.Random:
                if (_random.Next(2) == 0) a = _random.Next(StepCount); else b = _random.Next(StepCount);
                break;
            default: // A, then B
                if (i < StepCount) a = i; else b = i - StepCount;
                break;
        }

        double control = Knobs[2][c];
        int velocity = PlainVelocity;
        if (Target == BqTarget.Velocity)
            velocity = 1 + (int)Math.Round(control * 126);
        else
            SendControl(Target switch { BqTarget.Cutoff => 74, BqTarget.Resonance => 71, _ => 1 }, (int)Math.Round(control * 127));

        if (a >= 0) PlayRow(0, a, velocity, play);
        if (b >= 0) PlayRow(1, b, velocity, play);

        _position = new BqPosition(a, b, c);
        return _position;
    }

    void PlayRow(int row, int step, int velocity, Action<int, int, int> play)
    {
        int portamento = (int)Math.Round(Portamento[row] * 127);
        if (portamento != _sentPortamento)
        {
            _sentPortamento = portamento;
            _midi.Send([(byte)(0xB0 | Channel), 5, (byte)portamento]);                // portamento time
            _midi.Send([(byte)(0xB0 | Channel), 65, (byte)(portamento > 0 ? 127 : 0)]); // portamento on/off
        }
        play(Channel, NoteOf(row, step), velocity);
    }

    void SendControl(int controller, int value)
    {
        if (value == _sentControl) return;
        _sentControl = value;
        _midi.Send([(byte)(0xB0 | Channel), (byte)controller, (byte)value]);
    }

    /// <summary>The note a knob of row A (0) or B (1) plays: its position quantised to semitones of the range.</summary>
    public int NoteOf(int row, int step) =>
        Math.Clamp(BaseNote + (int)Math.Round(Knobs[row][step] * Range[row] * 12), 0, 127);

    // ---------------------------------------------------------------- persistence

    static readonly string[] Keys = ["knobs", "range", "porta", "mode", "target", "duty", "base"];

    string Key(string name) => $"bq{Slot}.{name}";

    /// <summary>Removes the stored settings (the sequencer is deleted).</summary>
    public void Forget()
    {
        foreach (var name in Keys)
            Preferences.Default.Remove(Key(name));
    }

    public void Save()
    {
        var p = Preferences.Default;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        p.Set(Key("knobs"), string.Join(';', Knobs.SelectMany(row => row).Select(v => v.ToString("0.###", inv))));
        p.Set(Key("range"), $"{Range[0]};{Range[1]}");
        p.Set(Key("porta"), string.Join(';', Portamento.Select(v => v.ToString("0.###", inv))));
        p.Set(Key("mode"), (int)Mode);
        p.Set(Key("target"), (int)Target);
        p.Set(Key("duty"), Duty);
        p.Set(Key("base"), BaseNote);
    }

    void Load()
    {
        var p = Preferences.Default;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var knobs = p.Get(Key("knobs"), "").Split(';');
        if (knobs.Length == 3 * StepCount)
            for (int i = 0; i < knobs.Length; i++)
                if (double.TryParse(knobs[i], inv, out double v))
                    Knobs[i / StepCount][i % StepCount] = Math.Clamp(v, 0, 1);

        var range = p.Get(Key("range"), "").Split(';');
        for (int row = 0; row < Math.Min(range.Length, 2); row++)
            if (int.TryParse(range[row], out int octaves) && Ranges.Contains(octaves))
                Range[row] = octaves;

        var porta = p.Get(Key("porta"), "").Split(';');
        for (int row = 0; row < Math.Min(porta.Length, 2); row++)
            if (double.TryParse(porta[row], inv, out double v))
                Portamento[row] = Math.Clamp(v, 0, 1);

        int mode = p.Get(Key("mode"), (int)Mode);
        Mode = Enum.IsDefined((BqMode)mode) ? (BqMode)mode : BqMode.Continuous;
        int target = p.Get(Key("target"), (int)Target);
        Target = Enum.IsDefined((BqTarget)target) ? (BqTarget)target : BqTarget.Velocity;
        Duty = p.Get(Key("duty"), Duty);
        BaseNote = Math.Clamp(p.Get(Key("base"), BaseNote), 0, 96);
    }
}
