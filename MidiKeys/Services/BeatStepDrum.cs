namespace MidiKeys.Services;

/// <summary>One of the 16 drum tracks: the note its pad plays and per step on/off, velocity, gate and shift.</summary>
public sealed class BeatStepTrack
{
    public int Note { get; set; }
    public bool Mute { get; set; }
    public bool[] On { get; } = new bool[BeatStepSequence.MaxSteps];
    public int[] Velocities { get; } = Enumerable.Repeat(100, BeatStepSequence.MaxSteps).ToArray();

    /// <summary>Per step 5–95 % of a step.</summary>
    public int[] Gates { get; } = Enumerable.Repeat(25, BeatStepSequence.MaxSteps).ToArray();

    /// <summary>Per step 0–75 % of a step later.</summary>
    public int[] Shifts { get; } = new int[BeatStepSequence.MaxSteps];
}

/// <summary>
/// The drum sequence of a BeatStep Pro on one MIDI channel: 16 tracks, one per pad, each with its
/// own note (General MIDI drums from 36 by default). All tracks share the length, the mode, the
/// time division, swing, randomness and probability. The encoders and step buttons edit the track
/// of the selected pad.
/// </summary>
public sealed class BeatStepDrum : BeatStepSequence
{
    public const int TrackCount = 16;
    public const int Velocity = 0, Gate = 1, Shift = 2;
    public const int MinGate = 5, MaxGate = 95, MaxShift = 75;

    static readonly Dictionary<int, string> DrumNames = new()
    {
        [35] = "KICK 2", [36] = "KICK", [37] = "RIM", [38] = "SNARE", [39] = "CLAP", [40] = "SNARE 2",
        [41] = "TOM LF", [42] = "HH CL", [43] = "TOM HF", [44] = "HH PED", [45] = "TOM L", [46] = "HH OP",
        [47] = "TOM LM", [48] = "TOM HM", [49] = "CRASH", [50] = "TOM H", [51] = "RIDE", [52] = "CHINA",
        [53] = "BELL", [54] = "TAMB", [55] = "SPLASH", [56] = "COWBELL", [57] = "CRASH 2", [59] = "RIDE 2",
        [60] = "BONGO H", [61] = "BONGO L", [62] = "CONGA M", [63] = "CONGA H", [64] = "CONGA L",
        [69] = "CABASA", [70] = "MARACAS", [75] = "CLAVES", [76] = "BLOCK H", [77] = "BLOCK L",
    };

    public BeatStepTrack[] Tracks { get; } = Enumerable.Range(0, TrackCount).Select(i => new BeatStepTrack { Note = 36 + i }).ToArray();

    /// <summary>The pad whose track the encoders and step buttons edit.</summary>
    public int Selected { get; set; }

    /// <summary>A pad tap while running sets the step that plays.</summary>
    public bool Record { get; set; }

    BeatStepTrack Track => Tracks[Math.Clamp(Selected, 0, TrackCount - 1)];

    public BeatStepDrum(IMidiOutput midi, MusicClock clock, int channel, int slot) : base(midi, clock, channel, slot)
    {
        // A beat to start from: kick on the quarters, snare on 2 and 4, closed hihat on the eighths
        for (int i = 0; i < MaxSteps; i++)
        {
            Tracks[0].On[i] = i % 4 == 0;
            Tracks[2].On[i] = i % 8 == 4;
            Tracks[6].On[i] = i % 2 == 0;
        }
        Load();
    }

    public static string PadName(int note) =>
        DrumNames.TryGetValue(note, out var name) ? name : BeatStepSeq.NoteName(note);

    /// <summary>A pad tap (UI thread): plays its note, and while recording sets the step that plays.</summary>
    public void Hit(int track, int velocity = 100)
    {
        var t = Tracks[track];
        int note = t.Note;
        Midi.NoteOn(Channel, note, velocity);
        _ = Task.Delay(100).ContinueWith(_ => Midi.NoteOff(Channel, note));

        int pos = Position;
        if (Record && IsRunning && pos >= 0)
            lock (Lock)
            {
                t.On[pos] = true;
                t.Velocities[pos] = velocity;
            }
    }

    // ---------------------------------------------------------------- rows

    public override string[] RowNames => ["VELO", "GATE", "SHIFT"];

    public override int Get(int row, int step) => row switch
    {
        Velocity => Track.Velocities[step],
        Gate => Track.Gates[step],
        _ => Track.Shifts[step],
    };

    public override void Set(int row, int step, int value)
    {
        switch (row)
        {
            case Velocity: Track.Velocities[step] = Math.Clamp(value, 1, 127); break;
            case Gate: Track.Gates[step] = Math.Clamp(value, MinGate, MaxGate); break;
            default: Track.Shifts[step] = Math.Clamp(value, 0, MaxShift); break;
        }
    }

    public override string Text(int row, int step) => row switch
    {
        Velocity => $"{Track.Velocities[step]}",
        Gate => $"{Track.Gates[step]}%",
        _ => $"+{Track.Shifts[step]}%",
    };

    public override float Level(int row, int step) => row switch
    {
        Velocity => (Track.Velocities[step] - 1) / 126f,
        Gate => (Track.Gates[step] - MinGate) / (float)(MaxGate - MinGate),
        _ => Track.Shifts[step] / (float)MaxShift,
    };

    public override bool IsOn(int step) => Track.On[step];

    public override void Toggle(int step) => Track.On[step] = !Track.On[step];

    protected override void CopyStep(int from, int to)
    {
        foreach (var t in Tracks)
        {
            t.On[to] = t.On[from];
            t.Velocities[to] = t.Velocities[from];
            t.Gates[to] = t.Gates[from];
            t.Shifts[to] = t.Shifts[from];
        }
    }

    // ---------------------------------------------------------------- timing (clock thread)

    protected override void PlayStep(int step, double now, double at, double stepMs, double untilNext)
    {
        foreach (var t in Tracks)
        {
            if (t.Mute || !t.On[step]) continue;
            var v = Vary();
            if (v.Drop) continue;
            double on = at + t.Shifts[step] / 100.0 * stepMs;
            double off = on + Math.Max(5, stepMs * Math.Clamp(t.Gates[step] / 100.0 + v.Gate, 0.05, 0.95));
            Schedule(now, t.Note, Math.Clamp(t.Velocities[step] + v.Velocity, 1, 127), on, off);
        }
    }

    // ---------------------------------------------------------------- persistence

    protected override string Prefix => "bsd";

    protected override IEnumerable<string> Names => ["tracks"];

    public override void Save()
    {
        SaveCommon();
        // Tracks separated by '|': "note,mute" then per step "on,velo,gate,shift", separated by ';'.
        lock (Lock)
            Preferences.Default.Set(Key("tracks"), string.Join('|', Tracks.Select(t =>
                $"{t.Note},{(t.Mute ? 1 : 0)};" + string.Join(';', Enumerable.Range(0, MaxSteps).Select(i =>
                    $"{(t.On[i] ? 1 : 0)},{t.Velocities[i]},{t.Gates[i]},{t.Shifts[i]}")))));
    }

    void Load()
    {
        LoadCommon();
        var tracks = Preferences.Default.Get(Key("tracks"), "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        for (int ti = 0; ti < Math.Min(tracks.Length, TrackCount); ti++)
        {
            var t = Tracks[ti];
            var parts = tracks[ti].Split(';');
            var head = Numbers(parts[0]);
            if (head.Length == 2)
            {
                t.Note = Math.Clamp(head[0], 0, 127);
                t.Mute = head[1] != 0;
            }
            for (int i = 0; i < Math.Min(parts.Length - 1, MaxSteps); i++)
            {
                var v = Numbers(parts[i + 1]);
                if (v.Length != 4) continue;
                t.On[i] = v[0] != 0;
                t.Velocities[i] = Math.Clamp(v[1], 1, 127);
                t.Gates[i] = Math.Clamp(v[2], MinGate, MaxGate);
                t.Shifts[i] = Math.Clamp(v[3], 0, MaxShift);
            }
        }
    }
}
