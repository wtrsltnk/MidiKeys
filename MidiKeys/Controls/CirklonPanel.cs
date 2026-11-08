using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>The row of a P3 pattern that the screen shows and the drags edit.</summary>
public enum CirklonRow { Note, Velocity, Length, Delay, AuxA, AuxB, AuxC, AuxD }

/// <summary>What the step keys switch: the row's flag (gate, or the aux flag on an aux row), tie, skip, or the last step.</summary>
public enum CirklonKeys { Gate, Tie, Skip, Last }

/// <summary>
/// The pattern-edit page of a Sequentix Cirklon: on top a colour screen with the row view (a bar per
/// step, drag it up or down to change the value, like turning the step encoder), below it 16 white
/// step keys with LEDs. It fills the space it gets; the current step is drawn on a separate, cheap
/// layer (<see cref="Lights"/>), so the panel itself only redraws when something changes.
/// </summary>
public class CirklonPanel : GraphicsView, IDrawable
{
    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Row colours, after the colours of the cirklon logo on the screen.</summary>
    public static readonly Color[] RowColors =
    [
        Color.FromArgb("#FF4D4D"), Color.FromArgb("#FF9A3D"), Color.FromArgb("#FFE14D"), Color.FromArgb("#6BE36B"),
        Color.FromArgb("#4DD8FF"), Color.FromArgb("#4D8BFF"), Color.FromArgb("#A66BFF"), Color.FromArgb("#FF6BD8"),
    ];

    public static readonly string[] RowNames = ["note", "velo", "length", "delay", "aux A", "aux B", "aux C", "aux D"];

    static readonly Color Panel = Color.FromArgb("#141414");
    static readonly Color Bezel = Color.FromArgb("#2C2C2C");
    static readonly Color Screen = Color.FromArgb("#05070B");
    static readonly Color ScreenText = Color.FromArgb("#E8ECF4");
    static readonly Color ScreenDim = Color.FromArgb("#3A4050");
    static readonly Color Print = Color.FromArgb("#D8D8D2");
    static readonly Color KeyFace = Color.FromArgb("#ECECE6");
    static readonly Color KeyShade = Color.FromArgb("#B5B5AE");
    static readonly Color KeyUnused = Color.FromArgb("#6E6E6A");
    static readonly Color LedOff = Color.FromArgb("#4C4C4C");
    static readonly Color LedGate = Color.FromArgb("#8CFF4F");
    static readonly Color LedTie = Color.FromArgb("#FFE14D");
    static readonly Color LedSkip = Color.FromArgb("#FF5A8A");
    static readonly Color LedLast = Color.FromArgb("#FF9A3D");
    static readonly Color LedPlay = Color.FromArgb("#FF2A1A");

    readonly CirklonSequencer _ck;
    (int Bar, int Step) _position = (-1, -1);
    int _dragStep = -1;
    float _dragY;
    int _dragStart;

    public CirklonRow Row { get; set; }
    public CirklonKeys Keys { get; set; }

    /// <summary>The bar that is shown and edited.</summary>
    public int EditBar { get; set; }

    /// <summary>Layer on top of the panel that marks the current step; add it in the same cell.</summary>
    public GraphicsView Lights { get; }

    /// <summary>Raised when the user changed a value or a flag (to save the pattern).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a key press switched the keys back to <see cref="CirklonKeys.Gate"/> (after setting the last step).</summary>
    public event EventHandler? KeysChanged;

    public CirklonPanel(CirklonSequencer ck)
    {
        _ck = ck;
        Drawable = this;
        Lights = new GraphicsView { Drawable = new LightsLayer(this), InputTransparent = true };
        StartInteraction += (_, e) => OnPress(e.Touches[0]);
        DragInteraction += (_, e) => OnDrag(e.Touches[0]);
        EndInteraction += (_, _) => OnRelease();
    }

    public void ShowPosition(int bar, int step)
    {
        _position = (bar, step);
        Lights.Invalidate();
    }

    public void Refresh()
    {
        Invalidate();
        Lights.Invalidate();
    }

    CirklonBar Bar => _ck.Bars[Math.Clamp(EditBar, 0, _ck.Bars.Count - 1)];
    int AuxIndex => (int)Row - (int)CirklonRow.AuxA;
    bool IsAux => Row >= CirklonRow.AuxA;

    // ---------------------------------------------------------------- geometry (pixels)

    float W => (float)Width;
    float H => (float)Height;
    float ColW => (W - 16) / CirklonBar.StepCount;
    float ColX(int step) => 8 + (step + 0.5f) * ColW;
    float KeySize => Math.Min(ColW * 0.8f, Math.Clamp(H * 0.17f, 40f, 84f));
    float KeysTop => H - 8 - 16 - 14 - KeySize;
    float LedRowY => KeysTop + KeySize + 9;
    float NumberTop => LedRowY + 6;
    float ScreenBottom => KeysTop - 14;
    const float ScreenTop = 6, HeaderH = 30, ValueH = 22;
    float BarsTop => ScreenTop + HeaderH + 10;
    float BarsBottom => ScreenBottom - ValueH - 8;

    // ---------------------------------------------------------------- values per row

    int Get(CirklonStep s) => Row switch
    {
        CirklonRow.Note => s.Note,
        CirklonRow.Velocity => s.Velocity,
        CirklonRow.Length => s.Length,
        CirklonRow.Delay => s.Delay,
        _ => s.Aux[AuxIndex],
    };

    void Set(CirklonStep s, int value)
    {
        switch (Row)
        {
            case CirklonRow.Note: s.Note = Math.Clamp(value, 0, 127); break;
            case CirklonRow.Velocity: s.Velocity = Math.Clamp(value, 1, 127); break;
            case CirklonRow.Length: s.Length = Math.Clamp(value, 1, CirklonStep.MaxLength); break;
            case CirklonRow.Delay: s.Delay = Math.Clamp(value, 0, CirklonStep.LengthPerStep - 1); break;
            default: s.Aux[AuxIndex] = Math.Clamp(value, 0, 127); break;
        }
    }

    // Length counts in 48ths up to one step, then in quarters of a step, as on the Cirklon.
    static int LengthToIndex(int v) => v < CirklonStep.LengthPerStep ? v : CirklonStep.LengthPerStep + (v - CirklonStep.LengthPerStep) / 12;
    static int IndexToLength(int i) => i < CirklonStep.LengthPerStep ? Math.Max(1, i) : CirklonStep.LengthPerStep + (i - CirklonStep.LengthPerStep) * 12;

    /// <summary>Drag distance in pixels for one unit of the row (an encoder detent).</summary>
    float PixelsPerUnit => Row switch { CirklonRow.Note => 7, CirklonRow.Length or CirklonRow.Delay => 5, _ => 3 };

    /// <summary>Height of the bar, 0–1.</summary>
    float Level(CirklonStep s) => Row switch
    {
        CirklonRow.Note => s.Note / 127f,
        CirklonRow.Velocity => s.Velocity / 127f,
        CirklonRow.Length => s.Length < CirklonStep.LengthPerStep
            ? s.Length / (float)CirklonStep.LengthPerStep
            : Math.Min(1, 0.25f + 0.75f * (s.Length / (float)CirklonStep.LengthPerStep - 1) / 15f),
        CirklonRow.Delay => s.Delay / 47f,
        _ => s.Aux[AuxIndex] / 127f,
    };

    string Text(CirklonStep s) => Row switch
    {
        CirklonRow.Note => $"{NoteNames[s.Note % 12]}{s.Note / 12 - 1}",
        CirklonRow.Length => s.Length < CirklonStep.LengthPerStep
            ? $":{s.Length:00}"
            : $"{s.Length / CirklonStep.LengthPerStep}:{s.Length % CirklonStep.LengthPerStep / 12}",
        CirklonRow.Delay => $":{s.Delay:00}",
        _ => $"{Get(s)}",
    };

    /// <summary>Whether the step's bar is filled: the gate on the note rows, the aux flag on an aux row.</summary>
    bool Active(CirklonStep s) => IsAux ? s.AuxOn[AuxIndex] : s.Gate;

    // ---------------------------------------------------------------- input

    void OnPress(PointF p)
    {
        int step = Math.Clamp((int)((p.X - 8) / ColW), 0, CirklonBar.StepCount - 1);
        if (p.Y >= KeysTop - 6)
        {
            PressKey(step);
            return;
        }
        if (p.Y < BarsTop - 20) return;
        _dragStep = step;
        _dragY = p.Y;
        int value = Get(Bar.Steps[step]);
        _dragStart = Row == CirklonRow.Length ? LengthToIndex(value) : value;
    }

    void PressKey(int step)
    {
        var s = Bar.Steps[step];
        switch (Keys)
        {
            case CirklonKeys.Gate when IsAux: s.AuxOn[AuxIndex] = !s.AuxOn[AuxIndex]; break;
            case CirklonKeys.Gate: s.Gate = !s.Gate; break;
            case CirklonKeys.Tie: s.Tie = !s.Tie; break;
            case CirklonKeys.Skip: s.Skip = !s.Skip; break;
            case CirklonKeys.Last:
                Bar.LastStep = step + 1;
                Keys = CirklonKeys.Gate; // like releasing the LAST key
                KeysChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void OnDrag(PointF p)
    {
        if (_dragStep < 0) return;
        int units = (int)Math.Round((_dragY - p.Y) / PixelsPerUnit);
        int value = Row == CirklonRow.Length ? IndexToLength(Math.Max(1, _dragStart + units)) : _dragStart + units;
        var s = Bar.Steps[_dragStep];
        int before = Get(s);
        Set(s, value);
        if (Get(s) != before)
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
        if (W <= 0 || H <= 0 || BarsBottom <= BarsTop) return;
        canvas.FillColor = Panel;
        canvas.FillRoundedRectangle(0, 0, W, H, 6);

        DrawScreen(canvas);
        DrawKeys(canvas);
    }

    void DrawScreen(ICanvas canvas)
    {
        var bar = Bar;
        var color = RowColors[(int)Row];
        canvas.FillColor = Bezel;
        canvas.FillRoundedRectangle(4, ScreenTop - 2, W - 8, ScreenBottom - ScreenTop + 4, 6);
        canvas.FillColor = Screen;
        canvas.FillRectangle(8, ScreenTop + 2, W - 16, ScreenBottom - ScreenTop - 4);

        // Top line: row, direction, timebase, bar
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 17;
        canvas.FontColor = color;
        canvas.DrawString(RowNames[(int)Row] + (IsAux ? $"  cc#{_ck.AuxCc[AuxIndex]}" : ""), 18, ScreenTop + 4, W / 3, HeaderH,
            HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontColor = ScreenText;
        string direction = CirklonSequencer.DirectionNames[(int)_ck.Direction];
        string timebase = CirklonSequencer.Timebases[_ck.Timebase].Name;
        canvas.DrawString($"▸{direction}    {timebase}    bar {EditBar + 1}/{_ck.Bars.Count}    last {bar.LastStep}",
            W / 3, ScreenTop + 4, W * 2 / 3 - 18, HeaderH, HorizontalAlignment.Right, VerticalAlignment.Center);
        canvas.StrokeColor = ScreenDim;
        canvas.StrokeSize = 1;
        canvas.DrawLine(14, ScreenTop + HeaderH + 4, W - 14, ScreenTop + HeaderH + 4);

        // Row view: a bar per step, filled when the step is active, a narrow bar for a length under one step
        float height = BarsBottom - BarsTop;
        canvas.FontSize = Math.Min(15, ColW * 0.3f);
        for (int i = 0; i < CirklonBar.StepCount; i++)
        {
            var s = bar.Steps[i];
            bool used = i < bar.LastStep && !s.Skip;
            float cx = ColX(i);
            float bw = ColW * (Row == CirklonRow.Length && s.Length < CirklonStep.LengthPerStep ? 0.32f : 0.62f);
            float bh = Math.Max(2, Level(s) * height);
            var rect = new RectF(cx - bw / 2, BarsBottom - bh, bw, bh);

            if (used && Active(s))
            {
                canvas.FillColor = color;
                canvas.FillRectangle(rect);
            }
            else
            {
                canvas.StrokeColor = used ? color.WithAlpha(0.55f) : ScreenDim;
                canvas.StrokeSize = 1.5f;
                canvas.DrawRectangle(rect);
            }

            canvas.FontColor = used ? ScreenText : ScreenDim;
            canvas.DrawString(s.Skip ? "skip" : Text(s), cx - ColW / 2, BarsBottom + 6, ColW, ValueH, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Groups of four, as the encoders are grouped on the panel
        canvas.StrokeColor = ScreenDim;
        for (int g = 1; g < 4; g++)
        {
            float x = 8 + g * 4 * ColW;
            canvas.DrawLine(x, BarsTop - 4, x, BarsBottom + ValueH + 4);
        }
    }

    void DrawKeys(ICanvas canvas)
    {
        var bar = Bar;
        float k = KeySize;
        for (int i = 0; i < CirklonBar.StepCount; i++)
        {
            var s = bar.Steps[i];
            float cx = ColX(i), x = cx - k / 2;
            bool used = i < bar.LastStep;

            canvas.FillColor = KeyShade;
            canvas.FillRoundedRectangle(x, KeysTop, k, k, 4);
            canvas.FillColor = used ? KeyFace : KeyUnused;
            canvas.FillRoundedRectangle(x + 2, KeysTop + 1, k - 4, k - 6, 3);

            // LED in the key: the flag the keys switch now
            bool on = Keys switch
            {
                CirklonKeys.Tie => s.Tie,
                CirklonKeys.Skip => s.Skip,
                CirklonKeys.Last => i == bar.LastStep - 1,
                _ => Active(s),
            };
            Color lit = Keys switch
            {
                CirklonKeys.Tie => LedTie,
                CirklonKeys.Skip => LedSkip,
                CirklonKeys.Last => LedLast,
                _ => IsAux ? RowColors[(int)Row] : LedGate,
            };
            canvas.FillColor = on ? lit : LedOff;
            canvas.FillRoundedRectangle(cx - k * 0.13f, KeysTop + k * 0.12f, k * 0.26f, Math.Max(4, k * 0.08f), 1);

            // Under the key: the gate, so you still see the active steps while setting ties or skips
            if (Keys is CirklonKeys.Tie or CirklonKeys.Skip && s.Gate)
            {
                canvas.FillColor = LedGate;
                canvas.FillCircle(cx + k * 0.3f, LedRowY, 3);
            }

            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = 14;
            canvas.FontColor = used ? Print : KeyUnused;
            canvas.DrawString($"{i + 1}", cx - ColW / 2, NumberTop, ColW, 16, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>The red LED under the step that plays and a marker on the screen: cheap enough for every step.</summary>
    sealed class LightsLayer(CirklonPanel panel) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirty)
        {
            var (bar, step) = panel._position;
            if (step < 0 || bar != panel.EditBar || panel.W <= 0 || panel.BarsBottom <= panel.BarsTop) return;
            float cx = panel.ColX(step);
            canvas.FillColor = LedPlay;
            canvas.FillCircle(cx, panel.LedRowY, 4);
            canvas.FillColor = ScreenText;
            canvas.FillRectangle(cx - panel.ColW * 0.35f, panel.BarsTop - 8, panel.ColW * 0.7f, 3);
        }
    }
}
