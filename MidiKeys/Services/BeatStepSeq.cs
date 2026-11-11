namespace MidiKeys.Services;

/// <summary>
/// A melodic sequence of a BeatStep Pro (SEQ 1 / SEQ 2) on one MIDI channel: per step a pitch,
/// quantised to the chosen scale, a velocity and a gate length; a gate turned fully up ties the
/// note to the next step. Transpose shifts the whole sequence in semitones.
/// </summary>
public sealed class BeatStepSeq : BeatStepSequence
{
    public const int Pitch = 0, Velocity = 1, Gate = 2;
    public const int Tie = 100;             // a gate of 100 % ties the note to the next one
    public const int MinGate = 5;
    public const int LowNote = 24, HighNote = 108; // C1–C8

    public static readonly (string Name, int[] Intervals)[] Scales =
    [
        ("CHROMATIC", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]),
        ("MAJOR", [0, 2, 4, 5, 7, 9, 11]),
        ("MINOR", [0, 2, 3, 5, 7, 8, 10]),
        ("DORIAN", [0, 2, 3, 5, 7, 9, 10]),
        ("MIXOLYDIAN", [0, 2, 4, 5, 7, 9, 10]),
        ("HARM MINOR", [0, 2, 3, 5, 7, 8, 11]),
        ("BLUES", [0, 3, 5, 6, 7, 10]),
    ];

    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public int[] Notes { get; } = Enumerable.Repeat(60, MaxSteps).ToArray();
    public int[] Velocities { get; } = Enumerable.Repeat(100, MaxSteps).ToArray();

    /// <summary>Per step 5–100 % of a step; 100 is a tie.</summary>
    public int[] Gates { get; } = Enumerable.Repeat(50, MaxSteps).ToArray();

    public bool[] On { get; } = Enumerable.Repeat(true, MaxSteps).ToArray();

    /// <summary>Index in <see cref="Scales"/>.</summary>
    public int Scale { get; set; }

    /// <summary>−24 to +24 semitones.</summary>
    public int Transpose { get; set; }

    public BeatStepSeq(IMidiOutput midi, MusicClock clock, int channel, int slot) : base(midi, clock, channel, slot)
    {
        Load();
    }

    /// <summary>The note on the scale: the stored note, or the nearest scale note below it.</summary>
    public int Quantize(int note)
    {
        var scale = Scales[Math.Clamp(Scale, 0, Scales.Length - 1)].Intervals;
        while (note > 0 && !scale.Contains(note % 12))
            note--;
        return note;
    }

    public static string NoteName(int note) => $"{NoteNames[note % 12]}{note / 12 - 1}";

    // ---------------------------------------------------------------- rows

    public override string[] RowNames => ["PITCH", "VELO", "GATE"];

    public override int Get(int row, int step) => row switch
    {
        Pitch => Notes[step],
        Velocity => Velocities[step],
        _ => Gates[step],
    };

    public override void Set(int row, int step, int value)
    {
        switch (row)
        {
            case Pitch: Notes[step] = Math.Clamp(value, LowNote, HighNote); break;
            case Velocity: Velocities[step] = Math.Clamp(value, 1, 127); break;
            default: Gates[step] = Math.Clamp(value, MinGate, Tie); break;
        }
    }

    /// <summary>The pitch encoder moves from scale note to scale note.</summary>
    public override int Adjust(int row, int start, int units)
    {
        if (row != Pitch) return start + units;
        int note = Quantize(start);
        for (int i = 0; i < Math.Abs(units); i++)
        {
            int next = note + Math.Sign(units);
            while (next > LowNote && next < HighNote && Quantize(next) != next)
                next += Math.Sign(units);
            if (next < LowNote || next > HighNote) break;
            note = next;
        }
        return note;
    }

    public override string Text(int row, int step) => row switch
    {
        Pitch => NoteName(Quantize(Notes[step])),
        Velocity => $"{Velocities[step]}",
        _ => Gates[step] >= Tie ? "TIE" : $"{Gates[step]}%",
    };

    public override float Level(int row, int step) => row switch
    {
        Pitch => (Quantize(Notes[step]) - LowNote) / (float)(HighNote - LowNote),
        Velocity => (Velocities[step] - 1) / 126f,
        _ => (Gates[step] - MinGate) / (float)(Tie - MinGate),
    };

    public override float PixelsPerUnit(int row) => row == Pitch ? 12 : 3;

    public override bool IsOn(int step) => On[step];

    public override void Toggle(int step) => On[step] = !On[step];

    protected override void CopyStep(int from, int to)
    {
        Notes[to] = Notes[from];
        Velocities[to] = Velocities[from];
        Gates[to] = Gates[from];
        On[to] = On[from];
    }

    // ---------------------------------------------------------------- timing (clock thread)

    protected override void PlayStep(int step, double now, double at, double stepMs, double untilNext)
    {
        if (!On[step]) return;
        var v = Vary();
        if (v.Drop) return;

        int note = Math.Clamp(Quantize(Notes[step]) + Transpose, 0, 127);
        int velocity = Math.Clamp(Velocities[step] + v.Velocity, 1, 127);
        bool tie = Gates[step] >= Tie;
        double off = tie
            ? at + untilNext + TieOverlapMs
            : at + Math.Max(5, stepMs * Math.Clamp(Gates[step] / 100.0 + v.Gate, 0.05, 0.95));
        Schedule(now, note, velocity, at, off, tie);
    }

    // ---------------------------------------------------------------- persistence

    protected override string Prefix => "bsp";

    protected override IEnumerable<string> Names => ["steps", "scale", "transpose"];

    public override void Save()
    {
        SaveCommon();
        var p = Preferences.Default;
        // Per step "note,velo,gate,on", separated by ';'.
        lock (Lock)
            p.Set(Key("steps"), string.Join(';', Enumerable.Range(0, MaxSteps).Select(i =>
                $"{Notes[i]},{Velocities[i]},{Gates[i]},{(On[i] ? 1 : 0)}")));
        p.Set(Key("scale"), Scale);
        p.Set(Key("transpose"), Transpose);
    }

    void Load()
    {
        LoadCommon();
        var p = Preferences.Default;
        Scale = Math.Clamp(p.Get(Key("scale"), Scale), 0, Scales.Length - 1);
        Transpose = Math.Clamp(p.Get(Key("transpose"), Transpose), -24, 24);
        var steps = p.Get(Key("steps"), "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < Math.Min(steps.Length, MaxSteps); i++)
        {
            var v = Numbers(steps[i]);
            if (v.Length != 4) continue;
            Set(Pitch, i, v[0]);
            Set(Velocity, i, v[1]);
            Set(Gate, i, v[2]);
            On[i] = v[3] != 0;
        }
    }
}
