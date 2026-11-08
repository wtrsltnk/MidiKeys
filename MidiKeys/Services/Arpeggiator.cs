namespace MidiKeys.Services;

public enum ArpMode { Up, Down, UpDown, AsPlayed, Random }

public enum ArpRate { Quarter, Eighth, Sixteenth, EighthTriplet }

/// <summary>
/// Arpeggiator on the shared <see cref="MusicClock"/>: once started it plays the held keys one
/// after another while at least one is down (or, with Hold, the last chord until Hold is switched
/// off). An 8-step rhythm decides which steps play and which rest. Stopped, the keys play directly.
/// </summary>
public sealed class Arpeggiator : IClockClient, IInstrument
{
    public const int RhythmLength = 8;

    readonly record struct HeldNote(int Note, int Velocity);

    readonly IMidiOutput _midi;
    readonly MusicClock _clock;
    readonly SoundingNotes _sounding;
    readonly Dictionary<int, int> _direct = new(); // stopped: note -> channel it was sent on (UI thread)
    readonly object _lock = new();
    readonly Random _random = new();
    readonly List<HeldNote> _pressed = new(); // keys that are down, in the order they were played
    readonly List<HeldNote> _notes = new();   // what the arp plays: the pressed keys, or the latched chord

    bool _running;    // UI thread only
    bool _joined;     // UI thread only
    int _index;       // next note in the pattern
    int _rhythmPos;   // last rhythm step played, -1 = not started
    int _octaves = 1;
    double _gateLength = 0.5;

    public ArpMode Mode { get; set; }

    /// <summary>1–4: the pattern repeats this many octaves up.</summary>
    public int Octaves { get => _octaves; set => _octaves = Math.Clamp(value, 1, 4); }

    public ArpRate Rate { get; set; } = ArpRate.Sixteenth;

    /// <summary>Gate length as a fraction of a step (staccato … nearly legato).</summary>
    public double GateLength { get => _gateLength; set => _gateLength = Math.Clamp(value, 0.05, 0.95); }

    /// <summary>MIDI channel (0–15) this arp plays on.</summary>
    public int Channel { get; }

    /// <summary>Number the settings are stored under.</summary>
    public int Slot { get; }

    /// <summary>MIDI note of the leftmost key of the arp keyboard.</summary>
    public int BaseNote { get; set; } = 48;

    /// <summary>Per rhythm step: play (true) or rest.</summary>
    public bool[] Rhythm { get; } = Enumerable.Repeat(true, RhythmLength).ToArray();

    public bool Hold { get; private set; } = true;

    public bool IsRunning => _running;

    /// <summary>Raised on the thread that called Start/Stop.</summary>
    public event EventHandler? RunningChanged;

    /// <summary>Raised on the clock thread at every step: rhythm position, or -1 when the arp stops.</summary>
    public event Action<int>? StepChanged;

    /// <summary>Raised on the UI thread when the notes the arp plays change.</summary>
    public event EventHandler? NotesChanged;

    public Arpeggiator(IMidiOutput midi, MusicClock clock, int channel, int slot)
    {
        Channel = channel;
        Slot = slot;
        _midi = midi;
        _clock = clock;
        _sounding = new SoundingNotes(midi);
        Load();
    }

    /// <summary>The notes the arp plays, low to high.</summary>
    public IReadOnlyList<int> Notes
    {
        get { lock (_lock) return _notes.Select(n => n.Note).Order().ToList(); }
    }

    // ---------------------------------------------------------------- input (UI thread)

    public void Start()
    {
        if (_running) return;
        _running = true;
        ReleaseDirect(); // keys that are down now get arpeggiated instead
        RunningChanged?.Invoke(this, EventArgs.Empty);
        NotesUpdated();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        RunningChanged?.Invoke(this, EventArgs.Empty);
        NotesUpdated();
    }

    public void NoteOn(int note, int velocity)
    {
        if (!_running)
        {
            _direct[note] = Channel;
            _midi.NoteOn(Channel, note, velocity);
        }

        lock (_lock)
        {
            if (Hold && _pressed.Count == 0)
                _notes.Clear(); // first key of a new chord replaces the latched one
            _pressed.RemoveAll(n => n.Note == note);
            _pressed.Add(new HeldNote(note, velocity));
            if (!_notes.Exists(n => n.Note == note))
                _notes.Add(new HeldNote(note, velocity));
        }
        NotesUpdated();
    }

    public void NoteOff(int note)
    {
        if (_direct.Remove(note, out int channel))
            _midi.NoteOff(channel, note);

        lock (_lock)
        {
            _pressed.RemoveAll(n => n.Note == note);
            if (!Hold)
                _notes.RemoveAll(n => n.Note == note);
        }
        NotesUpdated();
    }

    /// <summary>Forget the keys that are down (e.g. when the page goes away); a held chord stays.</summary>
    public void ReleaseKeys()
    {
        ReleaseDirect();
        lock (_lock)
        {
            if (!Hold)
                _notes.RemoveAll(n => _pressed.Exists(p => p.Note == n.Note));
            _pressed.Clear();
        }
        NotesUpdated();
    }

    public void SetHold(bool hold)
    {
        lock (_lock)
        {
            Hold = hold;
            if (!hold)
            {
                _notes.Clear();
                _notes.AddRange(_pressed);
            }
        }
        NotesUpdated();
    }

    void ReleaseDirect()
    {
        foreach (var (note, channel) in _direct)
            _midi.NoteOff(channel, note);
        _direct.Clear();
    }

    void NotesUpdated()
    {
        bool any;
        lock (_lock)
        {
            any = _running && _notes.Count > 0;
            if (any && !_joined)
            {
                _index = 0;
                _rhythmPos = -1;
            }
        }

        // Join/Leave outside our lock: the clock thread takes the clock lock first, then ours.
        if (any && !_joined)
        {
            _joined = true;
            _clock.Join(this);
        }
        else if (!any && _joined)
        {
            _joined = false;
            _clock.Leave(this);
            StepChanged?.Invoke(-1);
        }
        NotesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- timing (clock thread)

    void IClockClient.ReleaseDue(double now) => _sounding.ReleaseDue(now);
    double IClockClient.NextRelease => _sounding.NextRelease;
    void IClockClient.ReleaseAll() => _sounding.ReleaseAll();

    static int TicksPerStep(ArpRate rate) => rate switch
    {
        ArpRate.Quarter => MusicClock.Ppqn,
        ArpRate.Eighth => MusicClock.Ppqn / 2,
        ArpRate.EighthTriplet => MusicClock.Ppqn / 3,
        _ => MusicClock.Ppqn / 4,
    };

    void IClockClient.Tick(long tick, double now, double tickMs)
    {
        int ticksPerStep = TicksPerStep(Rate);
        if (tick % ticksPerStep != 0) return;

        int pos;
        lock (_lock)
        {
            if (_notes.Count == 0) return;
            pos = _rhythmPos = (_rhythmPos + 1) % RhythmLength;
            if (Rhythm[pos])
            {
                var next = NextNote();
                _sounding.Play(Channel, next.Note, next.Velocity, now + Math.Max(5, tickMs * ticksPerStep * _gateLength));
            }
        }
        StepChanged?.Invoke(pos);
    }

    HeldNote NextNote()
    {
        var chord = Mode == ArpMode.AsPlayed ? _notes : _notes.OrderBy(n => n.Note).ToList();
        var pattern = new List<HeldNote>();
        for (int octave = 0; octave < _octaves; octave++)
            foreach (var n in chord)
                if (n.Note + 12 * octave <= 127)
                    pattern.Add(n with { Note = n.Note + 12 * octave });

        switch (Mode)
        {
            case ArpMode.Random:
                return pattern[_random.Next(pattern.Count)];
            case ArpMode.Down:
                pattern.Reverse();
                break;
            case ArpMode.UpDown when pattern.Count > 2:
                pattern.AddRange(pattern.Skip(1).Take(pattern.Count - 2).Reverse()); // C E G C' G E: no double top or bottom
                break;
        }

        if (_index >= pattern.Count) _index = 0;
        var note = pattern[_index];
        _index = (_index + 1) % pattern.Count;
        return note;
    }

    // ---------------------------------------------------------------- persistence

    static readonly string[] Keys = ["mode", "octaves", "rate", "gate", "base", "rhythm"];

    string Key(string name) => $"arp{Slot}.{name}";

    /// <summary>Removes the stored settings (the arp is deleted).</summary>
    public void Forget()
    {
        foreach (var name in Keys)
            Preferences.Default.Remove(Key(name));
    }

    public void Save()
    {
        var p = Preferences.Default;
        p.Set(Key("mode"), (int)Mode);
        p.Set(Key("octaves"), Octaves);
        p.Set(Key("rate"), (int)Rate);
        p.Set(Key("gate"), GateLength);
        p.Set(Key("base"), BaseNote);
        p.Set(Key("rhythm"), new string(Rhythm.Select(on => on ? '1' : '0').ToArray()));
    }

    void Load()
    {
        var p = Preferences.Default;
        int mode = p.Get(Key("mode"), (int)Mode);
        Mode = Enum.IsDefined((ArpMode)mode) ? (ArpMode)mode : ArpMode.Up;
        Octaves = p.Get(Key("octaves"), Octaves);
        int rate = p.Get(Key("rate"), (int)Rate);
        Rate = Enum.IsDefined((ArpRate)rate) ? (ArpRate)rate : ArpRate.Sixteenth;
        GateLength = p.Get(Key("gate"), GateLength);
        BaseNote = Math.Clamp(p.Get(Key("base"), BaseNote), 0, 108);

        var rhythm = p.Get(Key("rhythm"), "");
        for (int i = 0; i < Math.Min(rhythm.Length, RhythmLength); i++)
            Rhythm[i] = rhythm[i] != '0';
    }
}
