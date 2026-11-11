namespace MidiKeys.Services;

/// <summary>The playback modes of a BeatStep Pro sequence.</summary>
public enum BeatStepDirection { Forward, Reverse, Alternate, Random }

/// <summary>
/// What the BeatStep Pro sequencers share: up to 64 steps in four pages of 16, the playback mode,
/// the time division, swing, randomness and probability. A melodic sequence
/// (<see cref="BeatStepSeq"/>) and the drum sequence (<see cref="BeatStepDrum"/>) build on it.
/// The rows are what the 16 encoders edit; the step buttons switch the steps on and off.
/// Runs on the shared <see cref="MusicClock"/>.
/// </summary>
public abstract class BeatStepSequence : IClockClient, IInstrument
{
    public const int MaxSteps = 64;
    public const int PageSize = 16;
    public const int PageCount = MaxSteps / PageSize;
    protected const double TieOverlapMs = 5; // a tied note ends just after the next note-on

    /// <summary>Time divisions: name and length of a step in clock ticks.</summary>
    public static readonly (string Name, int Ticks)[] Rates =
    [
        ("1/4", 24), ("1/8", 12), ("1/16", 6), ("1/32", 3),
        ("1/4T", 16), ("1/8T", 8), ("1/16T", 4), ("1/32T", 2),
    ];

    public static readonly string[] DirectionNames = ["FWD", "REV", "ALT", "RND"];

    /// <summary>How randomness changes a note: velocity and gate offsets, or leaving it out.</summary>
    protected readonly record struct Variation(int Velocity, double Gate, bool Drop);

    readonly record struct Pending(double At, int Note, int Velocity, double OffAt, bool Legato);

    protected readonly IMidiOutput Midi;
    protected readonly MusicClock Clock;
    protected readonly object Lock = new();
    protected readonly Random Rng = new();
    readonly SoundingNotes _sounding;
    readonly List<Pending> _pending = new(); // swung and shifted note-ons (clock thread)

    volatile bool _running;
    volatile int _position = -1;
    int _index = -1;        // the step that played last
    bool _backwards;        // alternate: on the way back
    int _length = PageSize;
    double _swing = 0.5, _randomness, _probability = 0.5;

    protected BeatStepSequence(IMidiOutput midi, MusicClock clock, int channel, int slot)
    {
        Midi = midi;
        Clock = clock;
        _sounding = new SoundingNotes(midi);
        Channel = channel;
        Slot = slot;
    }

    public int Channel { get; }

    public int Slot { get; }

    /// <summary>1–64: the last step that plays.</summary>
    public int Length { get => _length; set => _length = Math.Clamp(value, 1, MaxSteps); }

    public BeatStepDirection Direction { get; set; }

    /// <summary>Index in <see cref="Rates"/>; 1/16 by default.</summary>
    public int Rate { get; set; } = 2;

    /// <summary>50–75 %: where the second step of each pair falls; 50 % is straight.</summary>
    public double Swing { get => _swing; set => _swing = Math.Clamp(value, 0.5, 0.75); }

    /// <summary>0–1: how big the changes are.</summary>
    public double Randomness { get => _randomness; set => _randomness = Math.Clamp(value, 0, 1); }

    /// <summary>0–1: how often something changes.</summary>
    public double Probability { get => _probability; set => _probability = Math.Clamp(value, 0, 1); }

    public bool IsRunning => _running;

    /// <summary>The step that plays now (0–63), or -1.</summary>
    public int Position => _position;

    /// <summary>Raised on the clock thread at every step.</summary>
    public event Action<int>? StepChanged;

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler? RunningChanged;

    // ---------------------------------------------------------------- what the encoders and step buttons edit

    /// <summary>The rows the encoders can edit, as printed above the row buttons.</summary>
    public abstract string[] RowNames { get; }

    public abstract int Get(int row, int step);

    /// <summary>Sets a value; it is clamped to the row's range.</summary>
    public abstract void Set(int row, int step, int value);

    /// <summary>The value <paramref name="units"/> encoder detents away from <paramref name="start"/>.</summary>
    public virtual int Adjust(int row, int start, int units) => start + units;

    /// <summary>The value as the panel prints it.</summary>
    public abstract string Text(int row, int step);

    /// <summary>The value as a position of the knob, 0–1.</summary>
    public abstract float Level(int row, int step);

    /// <summary>Drag distance in pixels for one detent of the encoder.</summary>
    public virtual float PixelsPerUnit(int row) => 3;

    public abstract bool IsOn(int step);

    public abstract void Toggle(int step);

    /// <summary>Copies the 16 steps of a page onto the next page and lengthens the sequence to it.</summary>
    public void CopyPage(int page)
    {
        if (page < 0 || page >= PageCount - 1) return;
        lock (Lock)
        {
            for (int i = 0; i < PageSize; i++)
                CopyStep(page * PageSize + i, (page + 1) * PageSize + i);
            Length = Math.Max(Length, (page + 2) * PageSize);
        }
    }

    protected abstract void CopyStep(int from, int to);

    // ---------------------------------------------------------------- transport

    public void Start()
    {
        if (_running) return;
        lock (Lock)
        {
            _index = -1;
            _backwards = false;
        }
        _running = true;
        Clock.Join(this);
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        Clock.Leave(this);
        _position = -1;
        RunningChanged?.Invoke(this, EventArgs.Empty);
        StepChanged?.Invoke(-1);
    }

    /// <summary>Back to the first step on the next step.</summary>
    public void Reset()
    {
        lock (Lock)
        {
            _index = -1;
            _backwards = false;
        }
    }

    // ---------------------------------------------------------------- timing (clock thread)

    void IClockClient.ReleaseDue(double now)
    {
        lock (Lock)
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
            lock (Lock)
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
        lock (Lock) _pending.Clear();
        _sounding.ReleaseAll();
    }

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        int ticksPerStep = Rates[Math.Clamp(Rate, 0, Rates.Length - 1)].Ticks;
        if (tick % ticksPerStep != 0) return; // on the grid of the time division, in step with the other views

        int pos;
        lock (Lock)
        {
            double stepMs = tickMs * ticksPerStep;
            long count = tick / ticksPerStep;
            double delay = SwingDelay(count, stepMs);
            double untilNext = stepMs - delay + SwingDelay(count + 1, stepMs);

            pos = _index = NextIndex(_index);
            _position = pos;

            // Randomness: now and then another step plays instead of this one
            int play = pos;
            if (Chance(_probability) && Chance(_randomness / 2))
                play = Rng.Next(Length);

            PlayStep(play, now, now + delay, stepMs, untilNext);
        }
        StepChanged?.Invoke(pos);
    }

    /// <summary>The second step of each pair comes later by the swing.</summary>
    double SwingDelay(long count, double stepMs) => count % 2 == 1 ? (2 * _swing - 1) * stepMs : 0;

    /// <summary>Plays a step. Clock thread, the lock is held.</summary>
    /// <param name="at">When the step starts, with the swing.</param>
    /// <param name="untilNext">Time from <paramref name="at"/> to the start of the next step.</param>
    protected abstract void PlayStep(int step, double now, double at, double stepMs, double untilNext);

    /// <summary>Per note: with the probability the note changes, by up to the randomness.</summary>
    protected Variation Vary()
    {
        if (_randomness <= 0 || !Chance(_probability))
            return default;
        double r = _randomness;
        return new Variation(
            (int)Math.Round((Rng.NextDouble() * 2 - 1) * r * 64),
            (Rng.NextDouble() * 2 - 1) * r * 0.5,
            Chance(r / 3));
    }

    bool Chance(double p) => p > 0 && Rng.NextDouble() < p;

    /// <summary>Plays a note now or queues it for later (swing, shift).</summary>
    protected void Schedule(double now, int note, int velocity, double at, double offAt, bool legato = false)
    {
        var p = new Pending(at, note, velocity, offAt, legato);
        if (at <= now + 0.5) Sound(p); else _pending.Add(p);
    }

    void Sound(Pending p)
    {
        if (!_sounding.TryTie(Channel, p.Note, p.OffAt, p.Legato))
            _sounding.Play(Channel, p.Note, p.Velocity, p.OffAt, p.Legato);
    }

    int NextIndex(int i)
    {
        int count = Length;
        if (i >= count) i = -1; // the sequence got shorter: start again
        switch (Direction)
        {
            case BeatStepDirection.Reverse:
                return i < 0 ? count - 1 : (i - 1 + count) % count;
            case BeatStepDirection.Alternate: // forward, then back, without playing the ends twice
                if (i < 0 || count == 1) { _backwards = false; return 0; }
                if (!_backwards && i >= count - 1) _backwards = true;
                else if (_backwards && i <= 0) _backwards = false;
                return _backwards ? i - 1 : i + 1;
            case BeatStepDirection.Random:
                return Rng.Next(count);
            default:
                return (i + 1) % count;
        }
    }

    // ---------------------------------------------------------------- persistence

    static readonly string[] CommonNames = ["length", "dir", "rate", "swing", "random", "prob"];

    /// <summary>"bsp0" or "bsd0": the start of the keys the settings are stored under.</summary>
    protected abstract string Prefix { get; }

    protected abstract IEnumerable<string> Names { get; }

    protected string Key(string name) => $"{Prefix}{Slot}.{name}";

    /// <summary>Removes the stored settings (the sequencer is deleted).</summary>
    public void Forget()
    {
        foreach (var name in CommonNames.Concat(Names))
            Preferences.Default.Remove(Key(name));
    }

    public abstract void Save();

    protected void SaveCommon()
    {
        var p = Preferences.Default;
        p.Set(Key("length"), Length);
        p.Set(Key("dir"), (int)Direction);
        p.Set(Key("rate"), Rate);
        p.Set(Key("swing"), Swing);
        p.Set(Key("random"), Randomness);
        p.Set(Key("prob"), Probability);
    }

    protected void LoadCommon()
    {
        var p = Preferences.Default;
        Length = p.Get(Key("length"), Length);
        int dir = p.Get(Key("dir"), 0);
        Direction = Enum.IsDefined((BeatStepDirection)dir) ? (BeatStepDirection)dir : BeatStepDirection.Forward;
        Rate = Math.Clamp(p.Get(Key("rate"), Rate), 0, Rates.Length - 1);
        Swing = p.Get(Key("swing"), Swing);
        Randomness = p.Get(Key("random"), Randomness);
        Probability = p.Get(Key("prob"), Probability);
    }

    /// <summary>Numbers separated by commas, missing or broken ones as 0.</summary>
    protected static int[] Numbers(string text) =>
        text.Split(',').Select(x => int.TryParse(x, out int n) ? n : 0).ToArray();
}
