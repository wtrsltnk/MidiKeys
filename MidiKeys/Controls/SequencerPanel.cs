using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// Step panel in the style of the Behringer 1601: black with orange lines, per step a gate-assign
/// slide switch on the three gate buses, the step number and a slider with a lit cap. It fills the
/// space it gets. The current step is drawn on a separate, cheap layer (<see cref="Lights"/>), so the
/// panel itself only redraws when something changes.
/// </summary>
public class SequencerPanel : GraphicsView, IDrawable
{
    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    static readonly (string Bus, string Meaning)[] BusLabels = [("GATE BUS 1", "NOTE"), ("GATE BUS 2", "ACCENT"), ("GATE BUS 3", "LEGATO")];

    static readonly Color Panel = Theme.Panel1601;
    static readonly Color Orange = Theme.Orange1601;
    static readonly Color Print = Theme.Silk;
    static readonly Color PrintDim = Theme.SilkDim;
    static readonly Color Body = Color.FromArgb("#1A1A1A");
    static readonly Color BodyEdge = Color.FromArgb("#3C3C3C");
    static readonly Color Knob = Color.FromArgb("#4A4A4A");
    static readonly Color Ridge = Color.FromArgb("#909090");
    static readonly Color CapWhite = Color.FromArgb("#F4F6FF");
    static readonly Color CapBlue = Color.FromArgb("#3D86FF");
    static readonly Color CapOff = Color.FromArgb("#3A3A3A");

    readonly StepSequencer _sequencer;
    int[] _positions = [-1, -1];
    int _dragColumn = -1;

    /// <summary>Layer on top of the panel that lights the cap of the current step(s); add it in the same cell.</summary>
    public GraphicsView Lights { get; }

    /// <summary>Raised when the user changed a step (to save the pattern).</summary>
    public event EventHandler? PatternChanged;

    public SequencerPanel(StepSequencer sequencer)
    {
        _sequencer = sequencer;
        Drawable = this;
        Lights = new GraphicsView { Drawable = new LightsLayer(this), InputTransparent = true };
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
        DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        EndInteraction += (_, _) => OnRelease();
    }

    public void ShowPositions(int[] positions)
    {
        _positions = positions;
        Lights.Invalidate();
    }

    /// <summary>Redraws the panel and the lights (after a change that is not a step).</summary>
    public void Refresh()
    {
        Invalidate();
        Lights.Invalidate();
    }

    // ---------------------------------------------------------------- geometry (pixels)

    // Top to bottom: gate-assign switches, step numbers, sliders, note names. Columns fill the width,
    // with half a column between the A and B halves; the sliders take the height that is left.
    float W => (float)Width;
    float H => (float)Height;
    float LabelW => Math.Clamp(W * 0.08f, 64f, 116f);
    float ColW => (W - LabelW - 8) / 16.5f;
    float ColX(int i) => LabelW + (i + 0.5f + (i >= 8 ? 0.5f : 0)) * ColW;
    const float Top = 10;
    float AssignH => Math.Clamp(H * 0.26f, 66f, 150f);
    float BusY(int bus) => Top + AssignH * (1 + 2 * bus) / 6f;
    float NumberTop => Top + AssignH + 12;
    float NumberH => Math.Clamp(H * 0.075f, 30f, 46f);
    float NoteH => Math.Clamp(H * 0.05f, 18f, 26f);
    float SlotTop => NumberTop + NumberH + 8;
    float SlotBottom => H - NoteH - 8;
    float CapH => Math.Clamp((SlotBottom - SlotTop) / 10f, 18f, 36f);
    float CapW => Math.Min(ColW * 0.55f, 36f);
    float CapLow => SlotBottom - CapH / 2;
    float CapHigh => SlotTop + CapH / 2;
    float PitchY(int pitch) => CapLow - pitch / (float)StepSequencer.PitchRange * (CapLow - CapHigh);

    int ColumnAt(float x)
    {
        for (int i = 0; i < StepSequencer.StepCount; i++)
            if (Math.Abs(x - ColX(i)) <= ColW / 2)
                return i;
        return -1;
    }

    int PitchAt(float y) =>
        Math.Clamp((int)Math.Round((CapLow - y) / (CapLow - CapHigh) * StepSequencer.PitchRange), 0, StepSequencer.PitchRange);

    /// <summary>Gate bus 1 = a note, 2 = an accented note, 3 = a legato note.</summary>
    static int BusOf(SequencerStep step) => step.Legato ? 2 : step.Accent ? 1 : 0;

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        int col = ColumnAt(p.X);
        if (col < 0 || SlotBottom <= SlotTop) return;
        var step = _sequencer.Steps[col];

        if (p.Y < NumberTop - 6)
        {
            // Gate assign: the bus line nearest to the tap.
            int bus = Math.Clamp((int)Math.Round((p.Y - BusY(0)) / (BusY(1) - BusY(0))), 0, 2);
            step.Accent = bus == 1;
            step.Legato = bus == 2;
        }
        else if (p.Y < SlotTop - 4)
        {
            // Step number: play → rest → skip → reset → play
            if (step.Mode == StepMode.Play && step.Gate) step.Gate = false;
            else if (step.Mode == StepMode.Play) { step.Gate = true; step.Mode = StepMode.Skip; }
            else if (step.Mode == StepMode.Skip) step.Mode = StepMode.Reset;
            else step.Mode = StepMode.Play;
        }
        else
        {
            _dragColumn = col;
            step.Pitch = PitchAt(p.Y);
            Refresh();
            return;
        }
        Refresh();
        PatternChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnDrag(PointF p)
    {
        if (_dragColumn < 0) return;
        var step = _sequencer.Steps[_dragColumn];
        int pitch = PitchAt(p.Y);
        if (step.Pitch == pitch) return;
        step.Pitch = pitch;
        Refresh();
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
        if (W <= 0 || H <= 0 || SlotBottom <= SlotTop) return;
        canvas.FillColor = Panel;
        canvas.FillRectangle(0, 0, W, H);
        canvas.StrokeColor = Orange;
        canvas.StrokeSize = 2;
        canvas.DrawRoundedRectangle(1, 1, W - 2, H - 2, 10);

        DrawLabels(canvas);

        // The three gate buses run along all steps; every step's switch picks one.
        canvas.StrokeColor = Orange;
        canvas.StrokeSize = 2.5f;
        for (int bus = 0; bus < 3; bus++)
            canvas.DrawLine(LabelW - 6, BusY(bus), W - 8, BusY(bus));

        // Scale lines across the sliders: every half octave, brighter on the octaves
        canvas.StrokeSize = 1.5f;
        for (int pitch = 0; pitch <= StepSequencer.PitchRange; pitch += 6)
        {
            canvas.StrokeColor = pitch % 12 == 0 ? Print : PrintDim;
            canvas.DrawLine(LabelW - 6, PitchY(pitch), W - 8, PitchY(pitch));
        }

        bool split = _sequencer.Split;
        if (split)
        {
            float x = (ColX(7) + ColX(8)) / 2;
            canvas.StrokeColor = Orange;
            canvas.StrokeSize = 2;
            canvas.DrawLine(x, Top, x, H - 8);
        }

        for (int i = 0; i < StepSequencer.StepCount; i++)
            DrawStep(canvas, i, split);
    }

    void DrawLabels(ICanvas canvas)
    {
        float x = 10, w = LabelW - 18;
        float small = Math.Clamp(AssignH / 9f, 10f, 13f);
        canvas.FontColor = Print;
        for (int bus = 0; bus < 3; bus++)
        {
            var (name, meaning) = BusLabels[bus];
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = small * 0.85f;
            canvas.FontColor = PrintDim;
            canvas.DrawString(name, x, BusY(bus) - small * 1.3f, w, small * 1.2f, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = small;
            canvas.FontColor = Print;
            canvas.DrawString(meaning, x, BusY(bus) + 1, w, small * 1.3f, HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 13;
        canvas.DrawString("STEP", x, NumberTop, w, NumberH, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString("PITCH", x, SlotTop, w, 20, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 12;
        for (int oct = 0; oct <= 2; oct++)
            canvas.DrawString(oct == 0 ? "0" : $"+{oct}", x, PitchY(12 * oct) - 16, w - 4, 14, HorizontalAlignment.Right, VerticalAlignment.Bottom);
    }

    void DrawStep(ICanvas canvas, int i, bool split)
    {
        var step = _sequencer.Steps[i];
        float cx = ColX(i);
        bool sounds = step.Gate && step.Mode != StepMode.Skip;

        // Gate-assign slide switch with a ridged knob on the chosen bus
        float bw = Math.Min(ColW * 0.42f, 30f);
        canvas.FillColor = Body;
        canvas.FillRoundedRectangle(cx - bw / 2, Top, bw, AssignH, 3);
        canvas.StrokeColor = BodyEdge;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(cx - bw / 2, Top, bw, AssignH, 3);
        float ky = BusY(BusOf(step)), kh = AssignH / 3f - 6;
        canvas.FillColor = Knob;
        canvas.FillRoundedRectangle(cx - bw / 2 + 2, ky - kh / 2, bw - 4, kh, 2);
        canvas.StrokeColor = Ridge;
        for (int r = -1; r <= 1; r++)
            canvas.DrawLine(cx - bw / 2 + 5, ky + r * kh / 4, cx + bw / 2 - 5, ky + r * kh / 4);

        // Step number, or what the step does instead of playing
        bool numbered = step.Mode == StepMode.Play && step.Gate;
        string label = step.Mode switch
        {
            StepMode.Skip => "SKIP",
            StepMode.Reset => "RESET",
            _ when !step.Gate => "REST",
            _ => split ? $"{(i < 8 ? 'A' : 'B')}{i % 8 + 1}" : $"{i + 1}",
        };
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = numbered ? Math.Min(NumberH * 0.5f, ColW * 0.4f) : Math.Min(NumberH * 0.36f, ColW * 0.22f);
        canvas.FontColor = numbered ? Print : Orange;
        canvas.DrawString(label, cx - ColW / 2, NumberTop, ColW, NumberH, HorizontalAlignment.Center, VerticalAlignment.Center);

        // Slider slot and cap
        float sw = Math.Max(6, ColW * 0.14f);
        canvas.FillColor = Colors.Black;
        canvas.FillRoundedRectangle(cx - sw / 2, SlotTop, sw, SlotBottom - SlotTop, sw / 2);
        DrawCap(canvas, cx, PitchY(step.Pitch), sounds ? CapWhite : CapOff);

        int note = _sequencer.BaseNote + step.Pitch;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = Math.Min(NoteH * 0.62f, ColW * 0.3f);
        canvas.FontColor = sounds ? Print : PrintDim;
        canvas.DrawString(note <= 127 ? $"{NoteNames[note % 12]}{note / 12 - 1}" : "–",
            cx - ColW / 2, SlotBottom + 4, ColW, NoteH, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    void DrawCap(ICanvas canvas, float cx, float cy, Color light)
    {
        canvas.FillColor = light;
        canvas.FillRoundedRectangle(cx - CapW / 2, cy - CapH / 2, CapW, CapH, 3);
        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 2;
        canvas.DrawLine(cx - CapW / 2 + 2, cy, cx + CapW / 2 - 2, cy);
    }

    /// <summary>Only the blue cap of the current step(s): a few shapes, cheap enough to redraw every step.</summary>
    sealed class LightsLayer(SequencerPanel panel) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            if (panel.W <= 0 || panel.SlotBottom <= panel.SlotTop) return;
            foreach (int i in panel._positions)
                if (i >= 0)
                    panel.DrawCap(canvas, panel.ColX(i), panel.PitchY(panel._sequencer.Steps[i].Pitch), CapBlue);
        }
    }
}
