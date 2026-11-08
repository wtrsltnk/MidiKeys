namespace MidiKeys.Services;

/// <summary>The order in which the steps of a Cirklon pattern play (manual 1.22, "Directions").</summary>
public enum CirklonDirection { Forward, ReverseA, ReverseB, Alternate, Pendulum, Random, Brownian, EitherWay }

/// <summary>One step of a P3 pattern: the values of the rows and the flags.</summary>
public sealed class CirklonStep
{
    public const int LengthPerStep = 48; // length and delay are in 48ths of a step
    public const int MaxLength = 16 * LengthPerStep;

    public int Note { get; set; } = 60;
    public int Velocity { get; set; } = 100;

    /// <summary>1–47 = part of a step; from 48 (one step) up in quarters of a step.</summary>
    public int Length { get; set; } = 24;

    /// <summary>0–47: the note starts this many 48ths of a step late.</summary>
    public int Delay { get; set; }

    public int[] Aux { get; } = [64, 64, 64, 64];
    public bool[] AuxOn { get; } = new bool[4];

    public bool Gate { get; set; }

    /// <summary>Sustain the note until just after the start of the next step.</summary>
    public bool Tie { get; set; }

    /// <summary>Pass over this step in every direction.</summary>
    public bool Skip { get; set; }

    public void CopyFrom(CirklonStep other)
    {
        (Note, Velocity, Length, Delay, Gate, Tie, Skip) =
            (other.Note, other.Velocity, other.Length, other.Delay, other.Gate, other.Tie, other.Skip);
        other.Aux.CopyTo(Aux, 0);
        other.AuxOn.CopyTo(AuxOn, 0);
    }
}

/// <summary>A bar of 16 steps; only the steps up to <see cref="LastStep"/> play.</summary>
public sealed class CirklonBar
{
    public const int StepCount = 16;

    public CirklonStep[] Steps { get; } = Enumerable.Range(0, StepCount).Select(_ => new CirklonStep()).ToArray();

    /// <summary>1–16.</summary>
    public int LastStep { get; set; } = StepCount;
}

/// <summary>
/// One track of a Sequentix Cirklon with a P3 pattern, on one MIDI channel: 1–16 bars of 16 steps,
/// each with note, velocity, length, delay and four aux rows (MIDI CCs), and the gate, tie, skip and
/// aux flags. Direction and timebase as on the Cirklon. Runs on the shared <see cref="MusicClock"/>.
/// </summary>
public sealed class CirklonSequencer : IClockClient, IInstrument
{
    public const int MaxBars = 16;
    const double TieOverlapMs = 5;

    /// <summary>Timebases: name and length of a step in half clock ticks (a whole note is 192).</summary>
    public static readonly (string Name, int HalfTicks)[] Timebases =
    [
        ("1", 192), ("2", 96), ("4", 48), ("8", 24), ("16", 12), ("32", 6), ("64", 3),
        ("2T", 64), ("4T", 32), ("8T", 16), ("16T", 8), ("32T", 4), ("64T", 2),
    ];

    /// <summary>The directions as the Cirklon names them.</summary>
    public static readonly string[] DirectionNames =
        ["forward", "reverse A", "reverse B", "alternate", "pendulum", "random", "brownian", "eitherway"];

    readonly record struct Pending(double At, int Note, int Velocity, double OffAt);

    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly SoundingNotes _sounding;
    readonly object _lock = new();
    readonly Random _random = new();
    readonly List<Pending> _pending = new(); // delayed note-ons (clock thread)
    readonly List<CirklonBar> _bars = [new CirklonBar()];

    volatile bool _running;
    long _startHalf = -1;   // half tick of the first step, -1 = waiting for the grid
    int _index = -1;        // position in the playable steps
    bool _backwards;        // alternate / pendulum: on the way back
    (int Bar, int Step) _position = (-1, -1);

    public IReadOnlyList<CirklonBar> Bars => _bars;

    public CirklonDirection Direction { get; set; }

    /// <summary>Index in <see cref="Timebases"/>; 16ths by default.</summary>
    public int Timebase { get; set; } = 4;

    /// <summary>The MIDI CC each aux row sends.</summary>
    public int[] AuxCc { get; } = [1, 74, 71, 10];

    public int Channel { get; }

    public int Slot { get; }

    public bool IsRunning => _running;

    /// <summary>Raised on the clock thread at every step: the bar and step that plays.</summary>
    public event Action<int, int>? StepChanged;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? RunningChanged;

    public CirklonSequencer(IMidiOutput midi, MusicClock clock, int channel, int slot)
    {
        _midi = midi;
        _clock = clock;
        _sounding = new SoundingNotes(midi);
        Channel = channel;
        Slot = slot;
        Load();
    }

    /// <summary>Makes the pattern 1–16 bars long; new bars start as a copy of the last one.</summary>
    public void SetBarCount(int count)
    {
        count = Math.Clamp(count, 1, MaxBars);
        lock (_lock)
        {
            while (_bars.Count > count)
                _bars.RemoveAt(_bars.Count - 1);
            while (_bars.Count < count)
            {
                var last = _bars[^1];
                var bar = new CirklonBar { LastStep = last.LastStep };
                for (int i = 0; i < CirklonBar.StepCount; i++)
                    bar.Steps[i].CopyFrom(last.Steps[i]);
                _bars.Add(bar);
            }
        }
    }

    public void Start()
    {
        if (_running) return;
        lock (_lock)
        {
            _startHalf = -1;
            _index = -1;
            _backwards = false;
        }
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
        lock (_lock)
        {
            _index = -1;
            _backwards = false;
            _position = (-1, -1);
        }
        if (!_running)
            StepChanged?.Invoke(-1, -1);
    }

    // ---------------------------------------------------------------- timing (clock thread)

    void IClockClient.ReleaseDue(double now)
    {
        lock (_lock)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.At > now) continue;
                Sound(p);
                _pending.RemoveAt(i--);
            }
        }
        _sounding.ReleaseDue(now);
    }

    double IClockClient.NextRelease
    {
        get
        {
            lock (_lock)
            {
                double next = _sounding.NextRelease;
                foreach (var p in _pending)
                    next = Math.Min(next, p.At);
                return next;
            }
        }
    }

    void IClockClient.ReleaseAll()
    {
        lock (_lock) _pending.Clear();
        _sounding.ReleaseAll();
    }

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        int stepHalf = Timebases[Math.Clamp(Timebase, 0, Timebases.Length - 1)].HalfTicks;
        double stepMs = tickMs * stepHalf / 2;

        // A tick has two half ticks; at the 64th timebase a step starts on either of them.
        for (int half = 0; half < 2; half++)
        {
            long h = 2 * tick + half;
            (int Bar, int Step) played;
            lock (_lock)
            {
                if (_startHalf < 0)
                {
                    if (h % stepHalf != 0) continue; // fall in on the grid of this timebase
                    _startHalf = h;
                }
                if ((h - _startHalf) % stepHalf != 0) continue;
                played = PlayNext(now + half * tickMs / 2, stepMs);
            }
            StepChanged?.Invoke(played.Bar, played.Step);
        }
    }

    /// <summary>Moves on one step in the pattern's direction and plays it. Caller holds the lock.</summary>
    (int Bar, int Step) PlayNext(double at, double stepMs)
    {
        var playable = new List<(int Bar, int Step)>();
        for (int b = 0; b < _bars.Count; b++)
            for (int s = 0; s < _bars[b].LastStep; s++)
                if (!_bars[b].Steps[s].Skip)
                    playable.Add((b, s));
        if (playable.Count == 0)
            return _position = (-1, -1);

        _index = NextIndex(_index, playable.Count);
        _position = playable[_index];
        var step = _bars[_position.Bar].Steps[_position.Step];

        // Aux rows send their controller at the start of the step; the note may come later (delay).
        for (int aux = 0; aux < 4; aux++)
            if (step.AuxOn[aux])
                _midi.Send([(byte)(0xB0 | Channel), (byte)AuxCc[aux], (byte)step.Aux[aux]]);

        if (step.Gate)
        {
            double on = at + step.Delay / (double)CirklonStep.LengthPerStep * stepMs;
            double off = step.Tie
                ? at + stepMs + TieOverlapMs
                : on + Math.Max(5, step.Length / (double)CirklonStep.LengthPerStep * stepMs);
            var note = new Pending(on, step.Note, step.Velocity, off);
            if (step.Delay == 0) Sound(note); else _pending.Add(note);
        }
        return _position;
    }

    void Sound(Pending p) => _sounding.Play(Channel, p.Note, p.Velocity, p.OffAt);

    int NextIndex(int i, int count)
    {
        if (i >= count) i = count - 1;
        switch (Direction)
        {
            case CirklonDirection.ReverseA: // from step 1, then backwards from the last step
                return i < 0 ? 0 : (i - 1 + count) % count;
            case CirklonDirection.ReverseB: // backwards from the last step
                return i < 0 ? count - 1 : (i - 1 + count) % count;
            case CirklonDirection.Alternate: // forward, then reverse B: the ends play twice
                if (i < 0) { _backwards = false; return 0; }
                if (!_backwards && i == count - 1) { _backwards = true; return i; }
                if (_backwards && i == 0) { _backwards = false; return 0; }
                return _backwards ? i - 1 : i + 1;
            case CirklonDirection.Pendulum: // forward, then back without playing the ends twice
                if (i < 0 || count == 1) { _backwards = false; return 0; }
                if (!_backwards && i >= count - 1) _backwards = true;
                else if (_backwards && i <= 0) _backwards = false;
                return _backwards ? i - 1 : i + 1;
            case CirklonDirection.Random:
                return _random.Next(count);
            case CirklonDirection.Brownian: // next 50 %, previous 25 %, the same 25 %
                if (i < 0) return 0;
                int roll = _random.Next(4);
                return roll < 2 ? (i + 1) % count : roll == 2 ? (i - 1 + count) % count : i;
            case CirklonDirection.EitherWay: // next or previous, 50 % each
                if (i < 0) return 0;
                return _random.Next(2) == 0 ? (i + 1) % count : (i - 1 + count) % count;
            default:
                return (i + 1) % count;
        }
    }

    // ---------------------------------------------------------------- persistence

    static readonly string[] Keys = ["bars", "dir", "timebase", "aux"];

    string Key(string name) => $"ck{Slot}.{name}";

    /// <summary>Removes the stored settings (the sequencer is deleted).</summary>
    public void Forget()
    {
        foreach (var name in Keys)
            Preferences.Default.Remove(Key(name));
    }

    public void Save()
    {
        var p = Preferences.Default;
        // Bars separated by '|': the last step, then per step "note,velo,length,delay,a,b,c,d,flags".
        lock (_lock)
            p.Set(Key("bars"), string.Join('|', _bars.Select(bar =>
                bar.LastStep + ";" + string.Join(';', bar.Steps.Select(s =>
                    $"{s.Note},{s.Velocity},{s.Length},{s.Delay},{string.Join(',', s.Aux)},{Flags(s)}")))));
        p.Set(Key("dir"), (int)Direction);
        p.Set(Key("timebase"), Timebase);
        p.Set(Key("aux"), string.Join(',', AuxCc));
    }

    static int Flags(CirklonStep s)
    {
        int flags = (s.Gate ? 1 : 0) | (s.Tie ? 2 : 0) | (s.Skip ? 4 : 0);
        for (int aux = 0; aux < 4; aux++)
            if (s.AuxOn[aux]) flags |= 8 << aux;
        return flags;
    }

    void Load()
    {
        var p = Preferences.Default;
        int dir = p.Get(Key("dir"), 0);
        Direction = Enum.IsDefined((CirklonDirection)dir) ? (CirklonDirection)dir : CirklonDirection.Forward;
        Timebase = Math.Clamp(p.Get(Key("timebase"), Timebase), 0, Timebases.Length - 1);

        var aux = p.Get(Key("aux"), "").Split(',');
        for (int i = 0; i < Math.Min(aux.Length, 4); i++)
            if (int.TryParse(aux[i], out int cc) && cc is >= 0 and <= 127)
                AuxCc[i] = cc;

        var bars = p.Get(Key("bars"), "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (bars.Length == 0) return;
        _bars.Clear();
        foreach (var text in bars.Take(MaxBars))
        {
            var bar = new CirklonBar();
            var parts = text.Split(';');
            if (int.TryParse(parts[0], out int last))
                bar.LastStep = Math.Clamp(last, 1, CirklonBar.StepCount);
            for (int i = 0; i < Math.Min(parts.Length - 1, CirklonBar.StepCount); i++)
            {
                var v = parts[i + 1].Split(',').Select(x => int.TryParse(x, out int n) ? n : 0).ToArray();
                if (v.Length != 9) continue;
                var s = bar.Steps[i];
                s.Note = Math.Clamp(v[0], 0, 127);
                s.Velocity = Math.Clamp(v[1], 1, 127);
                s.Length = Math.Clamp(v[2], 1, CirklonStep.MaxLength);
                s.Delay = Math.Clamp(v[3], 0, CirklonStep.LengthPerStep - 1);
                for (int a = 0; a < 4; a++)
                {
                    s.Aux[a] = Math.Clamp(v[4 + a], 0, 127);
                    s.AuxOn[a] = (v[8] & (8 << a)) != 0;
                }
                s.Gate = (v[8] & 1) != 0;
                s.Tie = (v[8] & 2) != 0;
                s.Skip = (v[8] & 4) != 0;
            }
            _bars.Add(bar);
        }
    }
}
