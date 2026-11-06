using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using MidiKeys.Services;
using AView = Android.Views.View;
using ACanvas = Android.Graphics.Canvas;
using APaint = Android.Graphics.Paint;
using AColor = Android.Graphics.Color;
using ARectF = Android.Graphics.RectF;

namespace MidiKeys.Controls;

/// <summary>
/// Native piano view with real multitouch and velocity estimation.
///
/// For contact area and accelerometer the note-on is delayed by a short window
/// (default 20 ms): within it we collect the largest contact area (a finger keeps
/// flattening after touch-down) and the impact peak in the accelerometer.
/// </summary>
public class PianoKeyboardView : AView
{
    static readonly int[] WhiteSemitones = [0, 2, 4, 5, 7, 9, 11];        // C D E F G A B
    static readonly bool[] BlackAfterWhite = [true, true, false, true, true, true, false];

    sealed class ActiveTouch
    {
        public int Note;
        public long DownTime;     // uptime ms
        public float Position;    // 0 = top, 1 = bottom of the key
        public float MaxSize;     // dp
        public float Accel;       // m/s²
        public int? Velocity;
        public bool Sounding;
    }

    readonly Dictionary<int, ActiveTouch> _touches = new();  // pointer id -> touch
    readonly Dictionary<int, int> _noteCounts = new(); // note -> number of sounding touches
    readonly ARectF _rect = new();

    readonly APaint _whitePaint = Fill(AColor.Rgb(250, 250, 245));
    readonly APaint _blackPaint = Fill(AColor.Rgb(30, 30, 36));
    readonly APaint _pressedPaint = Fill(AColor.Rgb(137, 180, 250)); // colour set per key from its velocity
    readonly APaint _borderPaint = new(Android.Graphics.PaintFlags.AntiAlias) { Color = AColor.Rgb(60, 60, 70) };
    readonly APaint _textPaint = new(Android.Graphics.PaintFlags.AntiAlias) { Color = AColor.Rgb(110, 110, 120) };

    AccelSampler? _accel;
    VelocityConfig _config = VelocityConfig.Default;
    int _baseNote = 48;
    int _whiteKeyCount = 15;
    float _density = 1f;

    public event Action<NoteEventArgs>? NoteOn;
    public event Action<int>? NoteOff;

    public PianoKeyboardView(Context context) : base(context) => Init();

    protected PianoKeyboardView(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) => Init();

    void Init()
    {
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _borderPaint.SetStyle(APaint.Style.Stroke);
        _borderPaint.StrokeWidth = 1.5f * _density;
        _textPaint.TextSize = 13f * _density;
        _textPaint.TextAlign = APaint.Align.Center;
        HapticFeedbackEnabled = true;
    }

    // ---------------------------------------------------------------- properties

    public int BaseNote
    {
        get => _baseNote;
        set
        {
            value = Math.Clamp(value, 0, 127);
            if (_baseNote == value) return;
            _baseNote = value;
            Invalidate();
        }
    }

    public int WhiteKeyCount
    {
        get => _whiteKeyCount;
        set
        {
            value = Math.Max(1, value);
            if (_whiteKeyCount == value) return;
            ReleaseAll();
            _whiteKeyCount = value;
            Invalidate();
        }
    }

    public VelocityConfig VelocityConfig
    {
        get => _config;
        set
        {
            _config = value ?? VelocityConfig.Default;
            UpdateSensor();
        }
    }

    bool NeedsWindow => _config.Mode switch
    {
        VelocityMode.ContactSize or VelocityMode.Accelerometer or VelocityMode.Calibrate => true,
        VelocityMode.Auto => _config.Calibration.SizeUsable || _config.Calibration.AccelUsable,
        _ => false,
    };

    bool NeedsAccel => _config.Mode switch
    {
        VelocityMode.Accelerometer or VelocityMode.Calibrate => true,
        VelocityMode.Auto => _config.Calibration.AccelUsable,
        _ => false,
    };

    // ---------------------------------------------------------------- sensor lifecycle

    void UpdateSensor()
    {
        if (IsAttachedToWindow && NeedsAccel)
        {
            _accel ??= new AccelSampler(Context!);
            _accel.Start();
        }
        else
        {
            _accel?.Stop();
        }
    }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        UpdateSensor();
    }

    protected override void OnDetachedFromWindow()
    {
        ReleaseAll();
        _accel?.Stop();
        base.OnDetachedFromWindow();
    }

    // ---------------------------------------------------------------- geometry

    float WhiteWidth => Width / (float)_whiteKeyCount;
    float BlackWidth => WhiteWidth * 0.6f;
    float BlackHeight => Height * 0.6f;

    static int WhiteSemitone(int whiteIndex) => 12 * (whiteIndex / 7) + WhiteSemitones[whiteIndex % 7];

    bool HasBlackAfter(int whiteIndex) => whiteIndex < _whiteKeyCount - 1 && BlackAfterWhite[whiteIndex % 7];

    (int Note, float Position)? HitTest(float x, float y)
    {
        if (Width == 0 || x < 0 || x >= Width || y < 0 || y >= Height)
            return null;

        float ww = WhiteWidth;
        int i = Math.Clamp((int)(x / ww), 0, _whiteKeyCount - 1);
        int semitone = WhiteSemitone(i);
        float position = y / Height;

        if (y < BlackHeight)
        {
            float half = BlackWidth / 2;
            if (HasBlackAfter(i) && x >= (i + 1) * ww - half)
                (semitone, position) = (WhiteSemitone(i) + 1, y / BlackHeight);
            else if (i > 0 && HasBlackAfter(i - 1) && x < i * ww + half)
                (semitone, position) = (WhiteSemitone(i - 1) + 1, y / BlackHeight);
        }

        int note = _baseNote + semitone;
        return note is >= 0 and <= 127 ? (note, position) : null;
    }

    float ContactSizeDp(MotionEvent e, int pointerIndex)
    {
        float major = e.GetTouchMajor(pointerIndex);
        // Some drivers don't report TouchMajor; GetSize is then a normalized 0..1 value.
        return major > 0 ? major / _density : e.GetSize(pointerIndex) * 100f;
    }

    // ---------------------------------------------------------------- drawing

    protected override void OnDraw(ACanvas canvas)
    {
        base.OnDraw(canvas);
        float ww = WhiteWidth, h = Height;

        var held = new Dictionary<int, int?>(); // note -> strongest velocity on it
        foreach (var t in _touches.Values)
            held[t.Note] = held.TryGetValue(t.Note, out var v) ? KeyColors.Strongest(v, t.Velocity) : t.Velocity;

        for (int i = 0; i < _whiteKeyCount; i++)
        {
            int note = _baseNote + WhiteSemitone(i);
            _rect.Set(i * ww, 0, (i + 1) * ww, h);
            canvas.DrawRect(_rect, held.TryGetValue(note, out var vel) ? PressedPaint(false, vel) : _whitePaint);
            canvas.DrawRect(_rect, _borderPaint);

            if (WhiteSemitone(i) % 12 == 0 && note <= 127)
                canvas.DrawText($"C{note / 12 - 1}", i * ww + ww / 2, h - 14 * _density, _textPaint);
        }

        float bw = BlackWidth, bh = BlackHeight, radius = 4 * _density;
        for (int i = 0; i < _whiteKeyCount; i++)
        {
            if (!HasBlackAfter(i)) continue;
            int note = _baseNote + WhiteSemitone(i) + 1;
            float left = (i + 1) * ww - bw / 2;
            _rect.Set(left, -radius, left + bw, bh); // start above top edge: only bottom corners round
            canvas.DrawRoundRect(_rect, radius, radius, held.TryGetValue(note, out var vel) ? PressedPaint(true, vel) : _blackPaint);
        }
    }

    APaint PressedPaint(bool black, int? velocity)
    {
        var (r, g, b) = KeyColors.Pressed(black, velocity);
        _pressedPaint.Color = AColor.Rgb(r, g, b);
        return _pressedPaint;
    }

    // ---------------------------------------------------------------- touch

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
            {
                Parent?.RequestDisallowInterceptTouchEvent(true);
                int idx = e.ActionIndex;
                if (HitTest(e.GetX(idx), e.GetY(idx)) is { } hit)
                    StartTouch(e.GetPointerId(idx), hit.Note, hit.Position, e.EventTime, ContactSizeDp(e, idx), inheritedVelocity: null);
                break;
            }
            case MotionEventActions.Move:
                for (int i = 0; i < e.PointerCount; i++)
                {
                    int id = e.GetPointerId(i);
                    var hit = HitTest(e.GetX(i), e.GetY(i));
                    _touches.TryGetValue(id, out var touch);

                    if (touch is not null && hit?.Note == touch.Note)
                    {
                        // Same key: keep collecting the contact area while the window is open.
                        if (!touch.Sounding)
                            touch.MaxSize = Math.Max(touch.MaxSize, ContactSizeDp(e, i));
                        continue;
                    }
                    if (touch is null && hit is null)
                        continue;

                    // Sliding to another key: new note with the same velocity, no extra delay.
                    int? inherited = null;
                    if (touch is not null)
                    {
                        EndTouch(id);
                        inherited = touch.Velocity;
                    }
                    if (hit is { } h)
                        StartTouch(id, h.Note, h.Position, e.EventTime, ContactSizeDp(e, i), inherited);
                }
                break;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                EndTouch(e.GetPointerId(e.ActionIndex));
                break;

            case MotionEventActions.Cancel:
                ReleaseAll();
                break;
        }
        return true;
    }

    void StartTouch(int pointerId, int note, float position, long downTime, float size, int? inheritedVelocity)
    {
        var touch = new ActiveTouch { Note = note, Position = position, DownTime = downTime, MaxSize = size };
        _touches[pointerId] = touch;

        if (inheritedVelocity is int v)
        {
            touch.Velocity = v;
            Sound(touch);
        }
        else if (NeedsWindow)
        {
            PostDelayed(() =>
            {
                if (_touches.TryGetValue(pointerId, out var current) && ReferenceEquals(current, touch))
                    Sound(touch);
            }, Math.Clamp(_config.WindowMs, 0, 100));
        }
        else
        {
            Sound(touch);
        }
        Invalidate();
    }

    void Sound(ActiveTouch touch)
    {
        if (touch.Sounding) return;

        if (_accel is not null && NeedsAccel)
            touch.Accel = _accel.Peak(touch.DownTime - 10, SystemClock.UptimeMillis());
        touch.Velocity ??= ComputeVelocity(touch);
        touch.Sounding = true;
        Invalidate(); // key colour follows the velocity

        _noteCounts.TryGetValue(touch.Note, out int count);
        _noteCounts[touch.Note] = count + 1;
        if (count == 0)
        {
            NoteOn?.Invoke(new NoteEventArgs(touch.Note, touch.Velocity.Value, touch.MaxSize, touch.Accel, touch.Position));
            // Haptic feedback would show up in the accelerometer itself: skip it when measuring.
            if (!NeedsAccel)
                PerformHapticFeedback(FeedbackConstants.KeyboardTap);
        }
    }

    void EndTouch(int pointerId)
    {
        if (!_touches.Remove(pointerId, out var touch)) return;

        // Released within the window: still play the note (short).
        if (!touch.Sounding)
            Sound(touch);

        if (_noteCounts.TryGetValue(touch.Note, out int count) && count > 1)
        {
            _noteCounts[touch.Note] = count - 1;
        }
        else
        {
            _noteCounts.Remove(touch.Note);
            NoteOff?.Invoke(touch.Note);
        }
        Invalidate();
    }

    public void ReleaseAll()
    {
        foreach (var id in _touches.Keys.ToList())
            EndTouch(id);
    }

    // ---------------------------------------------------------------- velocity

    int ComputeVelocity(ActiveTouch t) => VelocityMath.Compute(_config, t.MaxSize, t.Accel, t.Position);

    static APaint Fill(AColor color) => new(Android.Graphics.PaintFlags.AntiAlias) { Color = color };
}
