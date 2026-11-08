using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// Step panel in the style of the Behringer BQ-10: dark grey with white print, a row of LEDs for
/// steps 1–8 and three rows of black knobs (A, B, C), each row with its LED and a range switch.
/// Drag a knob up or down to turn it. It fills the space it gets; the LEDs are drawn on a separate,
/// cheap layer (<see cref="Lights"/>), so the panel itself only redraws when something changes.
/// </summary>
public class BqPanel : GraphicsView, IDrawable
{
    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    static readonly string[] RowNames = ["CH A", "CH B", "CH C"];
    static readonly string[] TargetNames = ["VEL", "CUT", "RES", "MOD"];

    static readonly Color Panel = Theme.PanelBq;
    static readonly Color Print = Theme.Silk;
    static readonly Color PrintDim = Theme.SilkDim;
    static readonly Color Line = Color.FromArgb("#6A6A6A");
    static readonly Color KnobRim = Color.FromArgb("#3C3C3C");
    static readonly Color KnobBody = Color.FromArgb("#101010");
    static readonly Color SwitchOff = Color.FromArgb("#2E2E2E");
    static readonly Color SwitchOn = Color.FromArgb("#E9E9E7");
    static readonly Color LedOn = Color.FromArgb("#FF3B1F");
    static readonly Color LedOff = Color.FromArgb("#4A4A4A");

    readonly BqSequencer _bq;
    BqPosition _position = BqPosition.None;
    int _dragRow = -1, _dragStep;
    float _dragY;
    double _dragValue;

    /// <summary>Layer on top of the panel with the step and channel LEDs; add it in the same cell.</summary>
    public GraphicsView Lights { get; }

    /// <summary>Raised when the user changed a knob or switch (to save the settings).</summary>
    public event EventHandler? Changed;

    public BqPanel(BqSequencer bq)
    {
        _bq = bq;
        Drawable = this;
        Lights = new GraphicsView { Drawable = new LightsLayer(this), InputTransparent = true };
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
        DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        EndInteraction += (_, _) => OnRelease();
    }

    public void ShowPosition(BqPosition position)
    {
        _position = position;
        Lights.Invalidate();
    }

    // ---------------------------------------------------------------- geometry (pixels)

    float W => (float)Width;
    float H => (float)Height;
    float LabelW => Math.Clamp(W * 0.15f, 112f, 176f);
    float ColW => (W - LabelW - 8) / BqSequencer.StepCount;
    float ColX(int step) => LabelW + (step + 0.5f) * ColW;
    float NumberH => Math.Clamp(H * 0.1f, 30f, 46f);
    float RowH => (H - NumberH - 8) / 3;
    float RowTop(int row) => NumberH + row * RowH;
    float NoteH => Math.Clamp(RowH * 0.17f, 16f, 26f);
    float KnobR => Math.Min(ColW * 0.36f, (RowH - NoteH) * 0.36f);
    float KnobY(int row) => RowTop(row) + (RowH - NoteH) / 2 + 2;

    // Left of each row, centred on its knobs: the name with its LED, the switch, a caption.
    const float NameH = 22, CaptionH = 16;
    float SwitchH => Math.Clamp(RowH * 0.28f, 30f, 44f);
    float BlockTop(int row) => KnobY(row) - (NameH + 6 + SwitchH + 2 + CaptionH) / 2;

    /// <summary>The row's switch: three range buttons (A, B) or four target buttons (C).</summary>
    RectF SwitchButton(int row, int index)
    {
        int count = row == 2 ? TargetNames.Length : BqSequencer.Ranges.Length;
        float x = 8, w = (LabelW - 20) / count;
        return new RectF(x + index * w, BlockTop(row) + NameH + 6, w - 3, SwitchH);
    }

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        int row = Math.Clamp((int)((p.Y - NumberH) / RowH), 0, 2);
        if (p.Y < NumberH) return;

        if (p.X < LabelW)
        {
            int count = row == 2 ? TargetNames.Length : BqSequencer.Ranges.Length;
            for (int i = 0; i < count; i++)
            {
                if (!SwitchButton(row, i).Inflate(2, 6).Contains(p)) continue;
                if (row == 2) _bq.Target = (BqTarget)i;
                else _bq.Range[row] = BqSequencer.Ranges[i];
                Invalidate();
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            return;
        }

        int step = Math.Clamp((int)((p.X - LabelW) / ColW), 0, BqSequencer.StepCount - 1);
        _dragRow = row;
        _dragStep = step;
        _dragY = p.Y;
        _dragValue = _bq.Knobs[row][step];
    }

    void OnDrag(PointF p)
    {
        if (_dragRow < 0) return;
        // A full turn takes about two and a half rows of travel.
        double value = Math.Clamp(_dragValue + (_dragY - p.Y) / (RowH * 2.5), 0, 1);
        ref double knob = ref _bq.Knobs[_dragRow][_dragStep];
        if (Math.Abs(knob - value) < 0.002) return;
        knob = value;
        Invalidate();
    }

    void OnRelease()
    {
        if (_dragRow < 0) return;
        _dragRow = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(ICanvas canvas, RectF dirty)
    {
        if (W <= 0 || H <= 0 || KnobR <= 4) return;
        canvas.FillColor = Panel;
        canvas.FillRoundedRectangle(0, 0, W, H, 6);

        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontColor = Print;
        canvas.FontSize = Math.Min(NumberH * 0.42f, 18f);
        for (int step = 0; step < BqSequencer.StepCount; step++)
            canvas.DrawString($"{step + 1}", ColX(step) - 30, 2, 60, NumberH * 0.55f, HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.StrokeColor = Line;
        canvas.StrokeSize = 1;
        for (int row = 1; row < 3; row++)
            canvas.DrawLine(8, RowTop(row), W - 8, RowTop(row));

        for (int row = 0; row < 3; row++)
        {
            DrawRowLabel(canvas, row);
            for (int step = 0; step < BqSequencer.StepCount; step++)
                DrawKnob(canvas, row, step);
        }
    }

    void DrawRowLabel(ICanvas canvas, int row)
    {
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 16;
        canvas.FontColor = Print;
        canvas.DrawString(RowNames[row], 30, BlockTop(row), LabelW - 30, NameH, HorizontalAlignment.Left, VerticalAlignment.Center);

        int count = row == 2 ? TargetNames.Length : BqSequencer.Ranges.Length;
        canvas.FontSize = 13;
        for (int i = 0; i < count; i++)
        {
            bool on = row == 2 ? (int)_bq.Target == i : _bq.Range[row] == BqSequencer.Ranges[i];
            var r = SwitchButton(row, i);
            canvas.FillColor = on ? SwitchOn : SwitchOff;
            canvas.FillRoundedRectangle(r, 2);
            canvas.FontColor = on ? Colors.Black : Print;
            string text = row == 2 ? TargetNames[i] : $"{BqSequencer.Ranges[i]}V";
            canvas.DrawString(text, r.X, r.Y, r.Width, r.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
        canvas.FontColor = PrintDim;
        canvas.FontSize = 11;
        string caption = row == 2 ? "CH C SENDS" : $"{_bq.Range[row]} OCT";
        var first = SwitchButton(row, 0);
        canvas.DrawString(caption, first.X, first.Bottom + 2, LabelW - 16, CaptionH, HorizontalAlignment.Left, VerticalAlignment.Top);
    }

    void DrawKnob(ICanvas canvas, int row, int step)
    {
        float cx = ColX(step), cy = KnobY(row), r = KnobR;
        double value = _bq.Knobs[row][step];

        // Scale ticks from 0 (7 o'clock) to +5 (5 o'clock)
        canvas.StrokeColor = Print;
        canvas.StrokeSize = 1.5f;
        for (int t = 0; t <= 10; t++)
        {
            var (sx, sy) = Direction(t / 10.0);
            canvas.DrawLine(cx + sx * r * 1.15f, cy + sy * r * 1.15f, cx + sx * r * 1.32f, cy + sy * r * 1.32f);
        }

        canvas.FillColor = KnobRim;
        canvas.FillCircle(cx, cy, r);
        canvas.FillColor = KnobBody;
        canvas.FillCircle(cx, cy, r * 0.82f);

        var (dx, dy) = Direction(value);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = Math.Max(2, r * 0.12f);
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(cx + dx * r * 0.2f, cy + dy * r * 0.2f, cx + dx * r * 0.85f, cy + dy * r * 0.85f);
        canvas.StrokeLineCap = LineCap.Butt;

        string label;
        if (row < 2)
        {
            int note = _bq.NoteOf(row, step);
            label = $"{NoteNames[note % 12]}{note / 12 - 1}";
        }
        else
            label = _bq.Target == BqTarget.Velocity ? $"{1 + (int)Math.Round(value * 126)}" : $"{(int)Math.Round(value * 127)}";

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = Math.Min(NoteH * 0.7f, 16f);
        canvas.FontColor = Print;
        canvas.DrawString(label, cx - ColW / 2, RowTop(row) + RowH - NoteH - 2, ColW, NoteH, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    /// <summary>Unit vector of a knob at <paramref name="value"/> 0–1: from -135° to +135° around the top.</summary>
    static (float X, float Y) Direction(double value)
    {
        double angle = (-135 + 270 * value) * Math.PI / 180;
        return ((float)Math.Sin(angle), (float)-Math.Cos(angle));
    }

    /// <summary>The step LEDs and the LEDs of the rows that are playing: a few circles, cheap enough for every step.</summary>
    sealed class LightsLayer(BqPanel panel) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            if (panel.W <= 0 || panel.KnobR <= 4) return;
            var pos = panel._position;
            float r = Math.Clamp(panel.NumberH * 0.14f, 4f, 7f);
            float y = panel.NumberH * 0.72f;
            for (int step = 0; step < BqSequencer.StepCount; step++)
            {
                canvas.FillColor = step == pos.A || step == pos.B ? LedOn : LedOff;
                canvas.FillCircle(panel.ColX(step), y, r);
            }

            int[] rows = [pos.A, pos.B, pos.C];
            for (int row = 0; row < 3; row++)
            {
                canvas.FillColor = rows[row] >= 0 ? LedOn : LedOff;
                canvas.FillCircle(17, panel.BlockTop(row) + NameH / 2, r);
            }
        }
    }
}
