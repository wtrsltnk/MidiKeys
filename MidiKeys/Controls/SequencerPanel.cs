using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// Hardware-style step panel: per step a LED, a pitch slider, three gate-bus switches and a
/// play/skip/reset switch, all drawn in one GraphicsView so it looks the same on every platform.
/// </summary>
public class SequencerPanel : GraphicsView, IDrawable
{
    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    static readonly string[] RowLabels = ["GATE", "ACCENT", "LEGATO", "MODE"];

    static readonly Color Panel = Color.FromArgb("#1B1B1F");
    static readonly Color Silk = Color.FromArgb("#E8E2D0");        // panel print
    static readonly Color SilkDim = Color.FromArgb("#77736A");
    static readonly Color Track = Color.FromArgb("#050506");
    static readonly Color Cap = Color.FromArgb("#D9D6CC");
    static readonly Color CapLine = Color.FromArgb("#E5483A");
    static readonly Color LedOn = Color.FromArgb("#FF3B30");
    static readonly Color LedOff = Color.FromArgb("#3A1D1D");
    static readonly Color SwitchOff = Color.FromArgb("#2C2C31");
    static readonly Color[] SwitchOn = [Color.FromArgb("#E5483A"), Color.FromArgb("#F0A030"), Color.FromArgb("#E8D44D")];

    enum Part { None, Slider, Gate, Accent, Legato, Mode }

    readonly StepSequencer _sequencer;
    int[] _positions = [-1, -1];
    int _dragColumn = -1;

    /// <summary>Raised when the user changed a step (to save the pattern).</summary>
    public event EventHandler? PatternChanged;

    public SequencerPanel(StepSequencer sequencer)
    {
        _sequencer = sequencer;
        Drawable = this;
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
        DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        EndInteraction += (_, _) => OnRelease();
    }

    public void ShowPositions(int[] positions)
    {
        _positions = positions;
        Invalidate();
    }

    // ---------------------------------------------------------------- geometry

    // Everything scales with the row height, so the panel fits a phone in landscape as well as a desktop window.
    float RowH => Math.Clamp((float)Height / 11f, 16f, 28f);
    float LabelW => RowH * 2.8f;
    float ColW => ((float)Width - LabelW) / StepSequencer.StepCount;
    float SliderTop => RowH * 2.1f;
    float SliderBottom => (float)Height - RowH * 5.6f;
    float ColX(int i) => LabelW + i * ColW;
    float RowY(int row) => SliderBottom + RowH * (1.3f + row * 1.05f); // row 0..3: gate, accent, legato, mode

    (int Column, Part Part) HitTest(PointF p)
    {
        if (p.X < LabelW || ColW <= 0) return (-1, Part.None);
        int col = Math.Clamp((int)((p.X - LabelW) / ColW), 0, StepSequencer.StepCount - 1);

        if (p.Y >= SliderTop - RowH * 0.6f && p.Y <= SliderBottom + RowH * 0.6f)
            return (col, Part.Slider);
        for (int row = 0; row < 4; row++)
            if (p.Y >= RowY(row) - 2 && p.Y <= RowY(row) + RowH * 0.85f + 2)
                return (col, (Part)((int)Part.Gate + row));
        return (col, Part.None);
    }

    int PitchAt(float y)
    {
        double t = (SliderBottom - y) / (SliderBottom - SliderTop);
        return Math.Clamp((int)Math.Round(t * StepSequencer.PitchRange), 0, StepSequencer.PitchRange);
    }

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        var (col, part) = HitTest(p);
        if (col < 0) return;
        var step = _sequencer.Steps[col];

        switch (part)
        {
            case Part.Slider:
                _dragColumn = col;
                step.Pitch = PitchAt(p.Y);
                break;
            case Part.Gate: step.Gate = !step.Gate; break;
            case Part.Accent: step.Accent = !step.Accent; break;
            case Part.Legato: step.Legato = !step.Legato; break;
            case Part.Mode: step.Mode = (StepMode)(((int)step.Mode + 1) % 3); break;
            default: return;
        }
        Invalidate();
        if (part != Part.Slider)
            PatternChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnDrag(PointF p)
    {
        if (_dragColumn < 0) return;
        int pitch = PitchAt(p.Y);
        var step = _sequencer.Steps[_dragColumn];
        if (step.Pitch == pitch) return;
        step.Pitch = pitch;
        Invalidate();
    }

    void OnRelease()
    {
        if (_dragColumn < 0) return;
        _dragColumn = -1;
        PatternChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(ICanvas canvas, RectF dirty)
    {
        float w = (float)Width, h = (float)Height, rh = RowH;
        if (w <= 0 || h <= 0 || SliderBottom <= SliderTop) return;

        canvas.FillColor = Panel;
        canvas.FillRoundedRectangle(0, 0, w, h, 8);

        float small = rh * 0.42f;
        canvas.FontSize = small;

        // Left-hand labels
        canvas.FontColor = Silk;
        canvas.DrawString("STEP", 0, rh * 0.9f, LabelW - 6, small * 1.4f, HorizontalAlignment.Right, VerticalAlignment.Center);
        canvas.DrawString("PITCH", 0, (SliderTop + SliderBottom) / 2 - small, LabelW - 6, small * 2, HorizontalAlignment.Right, VerticalAlignment.Center);
        for (int row = 0; row < 4; row++)
            canvas.DrawString(RowLabels[row], 0, RowY(row), LabelW - 6, rh * 0.85f, HorizontalAlignment.Right, VerticalAlignment.Center);

        // Octave lines across the slider area
        canvas.StrokeColor = SilkDim;
        canvas.StrokeSize = 1;
        for (int oct = 0; oct <= 2; oct++)
        {
            float y = SliderBottom - oct * 12f / StepSequencer.PitchRange * (SliderBottom - SliderTop);
            canvas.DrawLine(LabelW, y, w - 4, y);
        }

        bool split = _sequencer.Split;
        if (split)
        {
            canvas.StrokeColor = Silk;
            canvas.StrokeSize = 2;
            canvas.DrawLine(ColX(8), 4, ColX(8), h - 4);
        }

        for (int i = 0; i < StepSequencer.StepCount; i++)
            DrawStep(canvas, i, split);
    }

    void DrawStep(ICanvas canvas, int i, bool split)
    {
        var step = _sequencer.Steps[i];
        float rh = RowH, cw = ColW, x = ColX(i), cx = x + cw / 2;
        bool current = _positions[0] == i || _positions[1] == i;
        bool skipped = step.Mode == StepMode.Skip;

        // LED + step number (in 2×8 mode: A1–A8 / B1–B8)
        float r = rh * 0.24f;
        canvas.FillColor = current ? LedOn : LedOff;
        canvas.FillCircle(cx, rh * 0.45f, r);
        canvas.FontColor = skipped ? SilkDim : Silk;
        string number = split ? $"{(i < 8 ? 'A' : 'B')}{i % 8 + 1}" : $"{i + 1}";
        canvas.DrawString(number, x, rh * 0.9f, cw, rh * 0.6f, HorizontalAlignment.Center, VerticalAlignment.Center);

        // Slider: track + cap
        float trackW = Math.Max(4, cw * 0.1f);
        canvas.FillColor = Track;
        canvas.FillRoundedRectangle(cx - trackW / 2, SliderTop - 4, trackW, SliderBottom - SliderTop + 8, trackW / 2);

        float y = SliderBottom - step.Pitch / (float)StepSequencer.PitchRange * (SliderBottom - SliderTop);
        float capW = cw * 0.62f, capH = Math.Max(12, rh * 0.75f);
        canvas.FillColor = skipped ? SilkDim : Cap;
        canvas.FillRoundedRectangle(cx - capW / 2, y - capH / 2, capW, capH, 2);
        canvas.StrokeColor = CapLine;
        canvas.StrokeSize = 2;
        canvas.DrawLine(cx - capW / 2 + 2, y, cx + capW / 2 - 2, y);

        // Note name under the slider
        int note = _sequencer.BaseNote + step.Pitch;
        canvas.FontColor = step.Gate && !skipped ? Silk : SilkDim;
        canvas.DrawString(note <= 127 ? $"{NoteNames[note % 12]}{note / 12 - 1}" : "–",
            x, SliderBottom + rh * 0.35f, cw, rh * 0.8f, HorizontalAlignment.Center, VerticalAlignment.Center);

        // Gate-bus switches
        bool[] buses = [step.Gate, step.Accent, step.Legato];
        float sw = Math.Min(cw * 0.7f, rh * 1.4f), sh = rh * 0.85f;
        for (int row = 0; row < 3; row++)
        {
            canvas.FillColor = buses[row] ? SwitchOn[row] : SwitchOff;
            canvas.FillRoundedRectangle(cx - sw / 2, RowY(row), sw, sh, 3);
        }

        // Mode switch
        float my = RowY(3);
        canvas.FillColor = step.Mode == StepMode.Play ? SwitchOff : Color.FromArgb("#45475A");
        canvas.FillRoundedRectangle(cx - sw / 2, my, sw, sh, 3);
        canvas.FontColor = step.Mode == StepMode.Reset ? LedOn : Silk;
        canvas.FontSize = rh * 0.36f;
        string mode = step.Mode switch { StepMode.Skip => "SKIP", StepMode.Reset => "RST", _ => "▶" };
        canvas.DrawString(mode, cx - sw / 2, my, sw, sh, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.FontSize = rh * 0.42f;
    }
}
