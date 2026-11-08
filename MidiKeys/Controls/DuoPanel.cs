using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// The sequencer side of a Dato DUO: a light body with a ring of 8 big round step buttons around a
/// start/stop button, and a two-octave pentatonic keyboard of round coloured keys with two red arrow
/// buttons for the octave. Side by side in
/// landscape, stacked in portrait. The current step is drawn on a separate, cheap layer
/// (<see cref="Lights"/>), so the panel itself only redraws when something changes.
/// </summary>
public class DuoPanel : GraphicsView, IDrawable
{
    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>A colour per note of the pentatonic scale; the upper octave is a lighter shade.</summary>
    static readonly Color[] KeyColors =
    [
        Color.FromArgb("#F2544A"), Color.FromArgb("#FF9F3A"), Color.FromArgb("#F7CE2F"), Color.FromArgb("#4CC764"), Color.FromArgb("#3F9BFF"),
    ];

    static readonly Color Body = Color.FromArgb("#ECE9E2");
    static readonly Color BodyEdge = Color.FromArgb("#CFCAC0");
    static readonly Color Track = Color.FromArgb("#D6D1C7");
    static readonly Color StepOff = Color.FromArgb("#C9C4BA");
    static readonly Color Ink = Color.FromArgb("#2A2A2A");
    static readonly Color Center = Color.FromArgb("#2B2B2B");
    static readonly Color Running = Color.FromArgb("#E5483B");
    static readonly Color LedOff = Color.FromArgb("#BDB7AC");
    static readonly Color LedOn = Color.FromArgb("#FF2A1A");

    readonly DuoSequencer _duo;
    int _position = -1;

    /// <summary>The step that the keyboard writes to while the sequencer is stopped.</summary>
    public int EditStep { get; set; }

    public GraphicsView Lights { get; }

    /// <summary>A step was switched on or off (to save the pattern).</summary>
    public event EventHandler? Changed;

    /// <summary>The start/stop button in the middle of the ring.</summary>
    public event EventHandler? PlayPressed;

    /// <summary>A key of the scale, 0–9.</summary>
    public event EventHandler<int>? KeyPressed;

    /// <summary>+1 or −1: one of the red arrow buttons, an octave up or down.</summary>
    public event EventHandler<int>? OctavePressed;

    public DuoPanel(DuoSequencer duo)
    {
        _duo = duo;
        Drawable = this;
        Lights = new GraphicsView { Drawable = new LightsLayer(this), InputTransparent = true };
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
    }

    public void ShowPosition(int position)
    {
        _position = position;
        Lights.Invalidate();
    }

    public void Refresh()
    {
        Invalidate();
        Lights.Invalidate();
    }

    public static Color ColorOf(int key) =>
        key < 5 ? KeyColors[key] : Blend(KeyColors[key % 5], Colors.White, 0.4f);

    static Color Blend(Color a, Color b, float t) =>
        new(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t);

    // ---------------------------------------------------------------- geometry (pixels)

    float W => (float)Width;
    float H => (float)Height;
    bool Wide => W >= H * 1.25f;

    /// <summary>The square that holds the ring.</summary>
    RectF RingBox
    {
        get
        {
            float size = Wide ? Math.Min(H, W * 0.5f) : Math.Min(W, H * 0.6f);
            return Wide ? new RectF(0, (H - size) / 2, size, size) : new RectF((W - size) / 2, 0, size, size);
        }
    }

    RectF KeysBox
    {
        get
        {
            var ring = RingBox;
            return Wide ? new RectF(ring.Right + 12, 12, W - ring.Right - 24, H - 24) : new RectF(12, ring.Bottom + 12, W - 24, H - ring.Bottom - 24);
        }
    }

    float RingR => RingBox.Width / 2 - 12;
    PointF RingCenter => RingBox.Center;
    float StepR => RingR * 0.17f;
    float CenterR => RingR * 0.3f;

    /// <summary>Step 1 at the top, then clockwise.</summary>
    PointF StepCenter(int step, float radius)
    {
        double angle = (-90 + step * 45) * Math.PI / 180;
        return new PointF(RingCenter.X + radius * (float)Math.Cos(angle), RingCenter.Y + radius * (float)Math.Sin(angle));
    }

    PointF StepAt(int step) => StepCenter(step, RingR * 0.66f);
    PointF LedAt(int step) => StepCenter(step, RingR * 0.93f);

    // The keyboard is a grid of 2 rows × 6 columns: the red arrow buttons in the first column,
    // then five keys per row, the upper octave on top.
    (PointF Center, float Radius) Cell(int row, int col)
    {
        var box = KeysBox;
        float cellW = box.Width / 6, cellH = box.Height / 2;
        float r = Math.Min(cellW, cellH) * 0.42f;
        return (new PointF(box.X + (col + 0.5f) * cellW, box.Y + (row + 0.5f) * cellH), r);
    }

    (PointF Center, float Radius) KeyAt(int key) => Cell(key >= 5 ? 0 : 1, 1 + key % 5);

    /// <summary>The octave arrow: ▲ (+1) beside the top row, ▼ (−1) beside the bottom row.</summary>
    (PointF Center, float Radius) ArrowAt(int delta)
    {
        var (c, r) = Cell(delta > 0 ? 0 : 1, 0);
        return (c, r * 0.7f);
    }

    static bool Inside(PointF p, PointF c, float r) => (p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y) <= r * r;

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        if (Inside(p, RingCenter, CenterR))
        {
            PlayPressed?.Invoke(this, EventArgs.Empty);
            return;
        }
        for (int step = 0; step < DuoSequencer.StepCount; step++)
        {
            if (!Inside(p, StepAt(step), StepR * 1.2f)) continue;
            _duo.On[step] = !_duo.On[step];
            EditStep = step;
            Refresh();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        for (int key = 0; key < DuoSequencer.KeyCount; key++)
        {
            var (c, r) = KeyAt(key);
            if (Inside(p, c, r * 1.1f))
            {
                KeyPressed?.Invoke(this, key);
                return;
            }
        }
        foreach (int delta in new[] { 1, -1 })
        {
            var (c, r) = ArrowAt(delta);
            if (Inside(p, c, r * 1.3f))
            {
                OctavePressed?.Invoke(this, delta);
                return;
            }
        }
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(ICanvas canvas, RectF dirty)
    {
        if (W <= 0 || H <= 0 || RingR <= 20) return;
        canvas.FillColor = BodyEdge;
        canvas.FillRoundedRectangle(0, 0, W, H, 28);
        canvas.FillColor = Body;
        canvas.FillRoundedRectangle(3, 3, W - 6, H - 6, 26);

        DrawRing(canvas);
        DrawKeys(canvas);
    }

    void DrawRing(ICanvas canvas)
    {
        var c = RingCenter;
        canvas.StrokeColor = Track;
        canvas.StrokeSize = StepR * 0.5f;
        canvas.DrawCircle(c.X, c.Y, RingR * 0.66f);

        for (int step = 0; step < DuoSequencer.StepCount; step++)
        {
            var led = LedAt(step);
            canvas.FillColor = LedOff;
            canvas.FillCircle(led.X, led.Y, RingR * 0.035f);

            var at = StepAt(step);
            int key = _duo.Keys[step];
            bool on = _duo.On[step];
            canvas.FillColor = on ? ColorOf(key) : StepOff;
            canvas.FillCircle(at.X, at.Y, StepR);
            if (!on)
            {
                canvas.FillColor = ColorOf(key);
                canvas.FillCircle(at.X, at.Y, StepR * 0.25f); // the note it would play
            }

            if (step == EditStep && !_duo.IsRunning)
            {
                canvas.StrokeColor = Ink;
                canvas.StrokeSize = Math.Max(3, StepR * 0.12f);
                canvas.DrawCircle(at.X, at.Y, StepR + 6); // the step the keys write to
            }

            int note = _duo.NoteOf(key);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = StepR * 0.5f;
            canvas.FontColor = on ? Ink : Ink.WithAlpha(0.45f);
            canvas.DrawString($"{NoteNames[note % 12]}{note / 12 - 1}", at.X - StepR, at.Y - StepR * 0.4f, StepR * 2, StepR * 0.8f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Start/stop in the middle
        canvas.FillColor = _duo.IsRunning ? Running : Center;
        canvas.FillCircle(c.X, c.Y, CenterR);
        canvas.FillColor = Colors.White;
        float s = CenterR * 0.38f;
        if (_duo.IsRunning)
            canvas.FillRoundedRectangle(c.X - s * 0.8f, c.Y - s * 0.8f, s * 1.6f, s * 1.6f, 3);
        else
        {
            var play = new PathF();
            play.MoveTo(c.X - s * 0.7f, c.Y - s);
            play.LineTo(c.X + s, c.Y);
            play.LineTo(c.X - s * 0.7f, c.Y + s);
            play.Close();
            canvas.FillPath(play);
        }
    }

    void DrawKeys(ICanvas canvas)
    {
        foreach (int delta in new[] { 1, -1 })
        {
            var (c, r) = ArrowAt(delta);
            canvas.FillColor = BodyEdge;
            canvas.FillCircle(c.X, c.Y + 3, r);
            canvas.FillColor = Running;
            canvas.FillCircle(c.X, c.Y, r);
            float s = r * 0.42f;
            var arrow = new PathF();
            arrow.MoveTo(c.X - s, c.Y + delta * s * 0.5f);
            arrow.LineTo(c.X + s, c.Y + delta * s * 0.5f);
            arrow.LineTo(c.X, c.Y - delta * s * 0.7f);
            arrow.Close();
            canvas.FillColor = Colors.White;
            canvas.FillPath(arrow);
        }

        for (int key = 0; key < DuoSequencer.KeyCount; key++)
        {
            var (c, r) = KeyAt(key);
            canvas.FillColor = BodyEdge;
            canvas.FillCircle(c.X, c.Y + 3, r); // a little depth
            canvas.FillColor = ColorOf(key);
            canvas.FillCircle(c.X, c.Y, r);

            int note = _duo.NoteOf(key);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = r * 0.42f;
            canvas.FontColor = Ink;
            canvas.DrawString($"{NoteNames[note % 12]}{note / 12 - 1}", c.X - r, c.Y - r * 0.3f, r * 2, r * 0.6f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>The LED and a ring around the step that plays: cheap enough for every step.</summary>
    sealed class LightsLayer(DuoPanel panel) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            int step = panel._position;
            if (step < 0 || panel.W <= 0 || panel.RingR <= 20) return;
            var led = panel.LedAt(step);
            canvas.FillColor = LedOn;
            canvas.FillCircle(led.X, led.Y, panel.RingR * 0.045f);

            var at = panel.StepAt(step);
            canvas.StrokeColor = panel._duo.On[step] ? Colors.White : LedOn;
            float stroke = Math.Max(4, panel.StepR * 0.16f);
            canvas.StrokeSize = stroke;
            canvas.DrawCircle(at.X, at.Y, panel.StepR - stroke / 2);
        }
    }
}
