using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// The front of an Arturia BeatStep Pro: rows of 16 encoders (drag up or down to turn them), on the
/// drum sequence two rows of eight pads, and the 16 step buttons at the bottom. It shows one page
/// of 16 steps. A melodic sequence shows its pitch, velocity and gate encoders at once; the drum
/// sequence shows the row that is chosen, for the track of the selected pad. The light that moves
/// with every step is drawn on a separate, cheap layer (<see cref="Lights"/>).
/// </summary>
public class BeatStepPanel : GraphicsView, IDrawable
{
    static readonly Color Body = Color.FromArgb("#1B1C1E");
    static readonly Color Print = Color.FromArgb("#D9DAD5");
    static readonly Color PrintDim = Color.FromArgb("#6C6E72");
    static readonly Color KnobRim = Color.FromArgb("#3A3B3E");
    static readonly Color KnobBody = Color.FromArgb("#0E0E0F");
    static readonly Color TickOff = Color.FromArgb("#34363A");
    static readonly Color Rubber = Color.FromArgb("#4E5054");
    static readonly Color RubberDim = Color.FromArgb("#2C2D30");
    static readonly Color Pad = Color.FromArgb("#3A3C40");
    static readonly Color Lit = Color.FromArgb("#3BA8E8");
    static readonly Color LastLit = Color.FromArgb("#F07D19");
    static readonly Color Play = Color.FromArgb("#FF3B2F");

    const float Edge = 8, NumbersH = 16, ValueH = 20, CaptionH = 16;

    readonly BeatStepSequence _seq;
    readonly BeatStepDrum? _drum;
    int _position = -1;

    int _dragRow = -1, _dragStep = -1;
    float _dragY;
    readonly int[] _dragStart = new int[BeatStepSequence.MaxSteps];

    /// <summary>The row the drum encoders edit (a melodic sequence shows all rows).</summary>
    public int Row { get; set; }

    /// <summary>The page of 16 steps that is shown and edited (0–3).</summary>
    public int Page { get; set; }

    /// <summary>A step button sets the last step instead of switching the step.</summary>
    public bool LastMode { get; set; }

    /// <summary>An encoder moves the value of every active step by the same amount.</summary>
    public bool AllSteps { get; set; }

    /// <summary>Layer on top of the panel that marks the current step; add it in the same cell.</summary>
    public GraphicsView Lights { get; }

    /// <summary>Raised when the user changed a value or a step (to save the sequence).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a step button set the last step and ended <see cref="LastMode"/>.</summary>
    public event EventHandler? LastSet;

    /// <summary>A pad was tapped (drums only).</summary>
    public event EventHandler<int>? PadPressed;

    public BeatStepPanel(BeatStepSequence seq)
    {
        _seq = seq;
        _drum = seq as BeatStepDrum;
        Drawable = this;
        Lights = new GraphicsView { Drawable = new LightsLayer(this), InputTransparent = true };
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
        DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        EndInteraction += (_, _) => OnRelease();
    }

    public void ShowPosition(int step)
    {
        _position = step;
        Lights.Invalidate();
    }

    public void Refresh()
    {
        Invalidate();
        Lights.Invalidate();
    }

    int[] Rows => _drum is null ? Enumerable.Range(0, _seq.RowNames.Length).ToArray() : [Row];
    int StepAt(int column) => Page * BeatStepSequence.PageSize + column;

    // ---------------------------------------------------------------- geometry (pixels)

    float W => (float)Width;
    float H => (float)Height;
    float ColW => (W - 2 * Edge) / BeatStepSequence.PageSize;
    float ColX(int column) => Edge + (column + 0.5f) * ColW;
    float ButtonH => Math.Clamp(H * 0.12f, 36f, 76f);
    float ButtonW => Math.Min(ColW * 0.8f, ButtonH * 1.4f);
    float ButtonsTop => H - Edge - NumbersH - ButtonH;
    float TopArea => ButtonsTop - 14 - Edge;
    float KnobsH => _drum is null ? TopArea : TopArea * 0.42f;
    float BandH => KnobsH / Rows.Length;
    float BandTop(int band) => Edge + band * BandH;
    float KnobR => Math.Max(4, Math.Min(ColW * 0.34f, (BandH - CaptionH - ValueH - 6) / 2 / 1.3f));
    float KnobY(int band) => BandTop(band) + CaptionH + (BandH - CaptionH - ValueH) / 2;
    float PadsTop => Edge + KnobsH + 10;
    float PadsBottom => ButtonsTop - 14;
    float PadW => (W - 2 * Edge) / 8;
    float PadH => (PadsBottom - PadsTop) / 2;

    RectF PadRect(int pad) =>
        new(Edge + pad % 8 * PadW + 5, PadsTop + pad / 8 * PadH + 5, PadW - 10, PadH - 10);

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        int column = Math.Clamp((int)((p.X - Edge) / ColW), 0, BeatStepSequence.PageSize - 1);
        if (p.Y >= ButtonsTop - 6)
        {
            PressStep(column);
            return;
        }
        if (_drum is not null && p.Y >= PadsTop)
        {
            int pad = Math.Clamp((int)((p.X - Edge) / PadW), 0, 7) + (p.Y >= PadsTop + PadH ? 8 : 0);
            PadPressed?.Invoke(this, pad);
            Refresh();
            return;
        }
        if (p.Y >= Edge + KnobsH) return;

        int band = Math.Clamp((int)((p.Y - Edge) / BandH), 0, Rows.Length - 1);
        _dragRow = Rows[band];
        _dragStep = StepAt(column);
        _dragY = p.Y;
        for (int i = 0; i < BeatStepSequence.MaxSteps; i++)
            _dragStart[i] = _seq.Get(_dragRow, i);
    }

    void PressStep(int column)
    {
        int step = StepAt(column);
        if (LastMode)
        {
            _seq.Length = step + 1;
            LastMode = false;
            LastSet?.Invoke(this, EventArgs.Empty);
        }
        else
            _seq.Toggle(step);
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void OnDrag(PointF p)
    {
        if (_dragStep < 0) return;
        int units = (int)Math.Round((_dragY - p.Y) / _seq.PixelsPerUnit(_dragRow));
        bool changed = false;
        foreach (int step in AllSteps
                     ? Enumerable.Range(0, _seq.Length).Where(_seq.IsOn)
                     : [_dragStep])
        {
            int before = _seq.Get(_dragRow, step);
            _seq.Set(_dragRow, step, _seq.Adjust(_dragRow, _dragStart[step], units));
            changed |= _seq.Get(_dragRow, step) != before;
        }
        if (changed)
            Invalidate();
    }

    void OnRelease()
    {
        if (_dragStep < 0) return;
        _dragStep = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(ICanvas canvas, RectF dirty)
    {
        if (W <= 0 || H <= 0 || TopArea <= 40) return;
        canvas.FillColor = Body;
        canvas.FillRoundedRectangle(0, 0, W, H, 6);

        var rows = Rows;
        for (int band = 0; band < rows.Length; band++)
            DrawKnobRow(canvas, band, rows[band]);
        if (_drum is not null)
            DrawPads(canvas, _drum);
        DrawSteps(canvas);
    }

    void DrawKnobRow(ICanvas canvas, int band, int row)
    {
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 12;
        canvas.FontColor = Print;
        canvas.DrawString(_seq.RowNames[row], Edge + 6, BandTop(band), W / 3, CaptionH, HorizontalAlignment.Left, VerticalAlignment.Center);

        float r = KnobR, cy = KnobY(band);
        for (int c = 0; c < BeatStepSequence.PageSize; c++)
        {
            int step = StepAt(c);
            bool used = step < _seq.Length;
            float cx = ColX(c);
            float level = _seq.Level(row, step);

            // Value ring: the ticks up to the value light up
            canvas.StrokeSize = Math.Max(1.5f, r * 0.09f);
            for (int t = 0; t <= 10; t++)
            {
                var (sx, sy) = Direction(t / 10.0);
                canvas.StrokeColor = used && t / 10f <= level + 0.001f ? Lit : TickOff;
                canvas.DrawLine(cx + sx * r * 1.12f, cy + sy * r * 1.12f, cx + sx * r * 1.3f, cy + sy * r * 1.3f);
            }

            canvas.FillColor = KnobRim;
            canvas.FillCircle(cx, cy, r);
            canvas.FillColor = KnobBody;
            canvas.FillCircle(cx, cy, r * 0.84f);

            var (dx, dy) = Direction(level);
            canvas.StrokeColor = used ? Colors.White : PrintDim;
            canvas.StrokeSize = Math.Max(2, r * 0.12f);
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(cx + dx * r * 0.2f, cy + dy * r * 0.2f, cx + dx * r * 0.8f, cy + dy * r * 0.8f);
            canvas.StrokeLineCap = LineCap.Butt;

            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = Math.Min(15, ColW * 0.26f);
            canvas.FontColor = used ? Print : PrintDim;
            canvas.DrawString(_seq.Text(row, step), cx - ColW / 2, cy + r * 1.3f + 2, ColW, ValueH, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    void DrawPads(ICanvas canvas, BeatStepDrum drum)
    {
        if (PadH < 20) return;
        for (int pad = 0; pad < BeatStepDrum.TrackCount; pad++)
        {
            var track = drum.Tracks[pad];
            var rect = PadRect(pad);
            bool selected = pad == drum.Selected;

            canvas.FillColor = selected ? Lit : Pad;
            canvas.FillRoundedRectangle(rect, 6);
            if (track.Mute)
            {
                canvas.FillColor = Colors.Black.WithAlpha(0.45f);
                canvas.FillRoundedRectangle(rect, 6);
            }

            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = Math.Min(16, rect.Height * 0.22f);
            canvas.FontColor = selected ? Theme.Ink : track.Mute ? PrintDim : Print;
            canvas.DrawString(BeatStepDrum.PadName(track.Note), rect.X, rect.Y, rect.Width, rect.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 12;
            canvas.DrawString(track.Mute ? $"{pad + 1}  MUTE" : $"{pad + 1}", rect.X + 6, rect.Y + 2, rect.Width - 12, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    void DrawSteps(ICanvas canvas)
    {
        float bw = ButtonW, bh = ButtonH;
        for (int c = 0; c < BeatStepSequence.PageSize; c++)
        {
            int step = StepAt(c);
            bool used = step < _seq.Length;
            float cx = ColX(c), x = cx - bw / 2;

            bool lit = LastMode ? step == _seq.Length - 1 : used && _seq.IsOn(step);
            canvas.FillColor = lit ? (LastMode ? LastLit : Lit) : used ? Rubber : RubberDim;
            canvas.FillRoundedRectangle(x, ButtonsTop, bw, bh, 4);

            // Groups of four, as printed on the panel
            if (c % 4 == 0)
            {
                canvas.StrokeColor = PrintDim;
                canvas.StrokeSize = 1;
                canvas.DrawLine(x - 3, ButtonsTop - 8, x - 3 + ColW * 4 - 4, ButtonsTop - 8);
            }

            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = 13;
            canvas.FontColor = used ? Print : PrintDim;
            canvas.DrawString($"{step + 1}", cx - ColW / 2, ButtonsTop + bh + 1, ColW, NumbersH, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>Unit vector of a knob at <paramref name="value"/> 0–1: from -135° to +135° around the top.</summary>
    static (float X, float Y) Direction(double value)
    {
        double angle = (-135 + 270 * value) * Math.PI / 180;
        return ((float)Math.Sin(angle), (float)-Math.Cos(angle));
    }

    /// <summary>The step that plays (a red frame on its button) and the pads that hit on it.</summary>
    sealed class LightsLayer(BeatStepPanel panel) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            int pos = panel._position;
            if (pos < 0 || panel.W <= 0 || panel.TopArea <= 40) return;

            if (pos / BeatStepSequence.PageSize == panel.Page)
            {
                float cx = panel.ColX(pos % BeatStepSequence.PageSize);
                canvas.StrokeColor = Play;
                canvas.StrokeSize = 3;
                canvas.DrawRoundedRectangle(cx - panel.ButtonW / 2 + 1.5f, panel.ButtonsTop + 1.5f, panel.ButtonW - 3, panel.ButtonH - 3, 4);
            }

            if (panel._drum is not { } drum || panel.PadH < 20) return;
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 3;
            for (int pad = 0; pad < BeatStepDrum.TrackCount; pad++)
            {
                var track = drum.Tracks[pad];
                if (track.Mute || !track.On[pos]) continue;
                var r = panel.PadRect(pad);
                canvas.DrawRoundedRectangle(r.X + 1.5f, r.Y + 1.5f, r.Width - 3, r.Height - 3, 6);
            }
        }
    }
}
