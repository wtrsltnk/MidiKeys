using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MidiKeys.Services;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using TextAlignment = Microsoft.UI.Xaml.TextAlignment;
using WColor = Windows.UI.Color;
using WPoint = Windows.Foundation.Point;
using WRect = Windows.Foundation.Rect;
using VirtualKey = Windows.System.VirtualKey;

namespace MidiKeys.Controls;

/// <summary>
/// Native WinUI piano for mouse, pen and multitouch: every pointer is its own finger, so
/// chords and sliding over keys work like on Android. There is no accelerometer here;
/// contact size comes from touchscreens that report a contact rectangle.
/// </summary>
public class PianoKeyboardView : Canvas
{
    static readonly int[] WhiteSemitones = [0, 2, 4, 5, 7, 9, 11];        // C D E F G A B
    static readonly bool[] BlackAfterWhite = [true, true, false, true, true, true, false];

    sealed class ActiveTouch
    {
        public int Note;
        public double Position;   // 0 = top, 1 = bottom of the key
        public double MaxSize;    // contact diameter as reported by the digitizer
        public int? Velocity;
        public bool Sounding;
    }

    readonly Dictionary<uint, ActiveTouch> _touches = new();  // pointer id -> touch
    readonly Dictionary<int, int> _noteCounts = new();        // note -> number of sounding touches
    readonly Dictionary<int, (Rectangle Shape, bool Black)> _keys = new();

    readonly SolidColorBrush _whiteBrush = Brush(KeyColors.White);
    readonly SolidColorBrush _blackBrush = Brush(KeyColors.Black);
    readonly SolidColorBrush _borderBrush = Brush(KeyColors.Border);
    readonly SolidColorBrush _textBrush = Brush(KeyColors.Text);
    readonly SolidColorBrush _markedWhiteBrush = Brush(KeyColors.MarkedWhite);
    readonly SolidColorBrush _markedBlackBrush = Brush(KeyColors.MarkedBlack);
    HashSet<int> _marked = new();

    VelocityConfig _config = VelocityConfig.Default;
    int _baseNote = 48;
    int _whiteKeyCount = 15;

    public event Action<NoteEventArgs>? NoteOn;
    public event Action<int>? NoteOff;

    /// <summary>+1 for Page Up, −1 for Page Down.</summary>
    public event Action<int>? OctaveShift;

    public PianoKeyboardView()
    {
        // The keys themselves are hit-testable and their pointer events bubble up to us; we don't rely
        // on our own Background for that, because MAUI maps the (null) Background of PianoKeyboard onto it.
        SizeChanged += (_, _) => Rebuild();
        Loaded += (_, _) => AttachKeyboard();
        Unloaded += (_, _) => { ReleaseAll(); DetachKeyboard(); };

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerLost;
        PointerCaptureLost += OnPointerLost;
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
            Rebuild();
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
            Rebuild();
        }
    }

    /// <summary>Notes drawn light green; a pressed key still shows its velocity colour.</summary>
    public int[] MarkedNotes
    {
        get => _marked.ToArray();
        set
        {
            var marked = new HashSet<int>(value ?? []);
            if (marked.SetEquals(_marked)) return;
            _marked = marked;
            UpdateFills();
        }
    }

    public VelocityConfig VelocityConfig
    {
        get => _config;
        set => _config = value ?? VelocityConfig.Default;
    }

    bool NeedsWindow => _config.Mode switch
    {
        VelocityMode.ContactSize or VelocityMode.Calibrate => true,
        VelocityMode.Auto => _config.Calibration.SizeUsable,
        _ => false,
    };

    // ---------------------------------------------------------------- geometry

    double WhiteWidth => ActualWidth / _whiteKeyCount;
    double BlackWidth => WhiteWidth * 0.6;
    double BlackHeight => ActualHeight * 0.6;

    static int WhiteSemitone(int whiteIndex) => 12 * (whiteIndex / 7) + WhiteSemitones[whiteIndex % 7];

    bool HasBlackAfter(int whiteIndex) => whiteIndex < _whiteKeyCount - 1 && BlackAfterWhite[whiteIndex % 7];

    (int Note, double Position)? HitTest(WPoint p)
    {
        double x = p.X, y = p.Y;
        if (ActualWidth == 0 || x < 0 || x >= ActualWidth || y < 0 || y >= ActualHeight)
            return null;

        double ww = WhiteWidth;
        int i = Math.Clamp((int)(x / ww), 0, _whiteKeyCount - 1);
        int semitone = WhiteSemitone(i);
        double position = y / ActualHeight;

        if (y < BlackHeight)
        {
            double half = BlackWidth / 2;
            if (HasBlackAfter(i) && x >= (i + 1) * ww - half)
                (semitone, position) = (WhiteSemitone(i) + 1, y / BlackHeight);
            else if (i > 0 && HasBlackAfter(i - 1) && x < i * ww + half)
                (semitone, position) = (WhiteSemitone(i - 1) + 1, y / BlackHeight);
        }

        int note = _baseNote + semitone;
        return note is >= 0 and <= 127 ? (note, position) : null;
    }

    static double ContactSize(PointerPoint point)
    {
        var r = point.Properties.ContactRect; // empty for mouse and pen
        return Math.Max(r.Width, r.Height);
    }

    // ---------------------------------------------------------------- drawing

    void Rebuild()
    {
        Children.Clear();
        _keys.Clear();

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        Clip = new RectangleGeometry { Rect = new WRect(0, 0, w, h) };

        double ww = WhiteWidth;
        for (int i = 0; i < _whiteKeyCount; i++)
        {
            int note = _baseNote + WhiteSemitone(i);
            var key = new Rectangle { Width = ww, Height = h, Stroke = _borderBrush, StrokeThickness = 1.5 };
            Place(key, i * ww, 0);
            _keys[note] = (key, false);

            if (WhiteSemitone(i) % 12 == 0 && note <= 127)
            {
                var label = new TextBlock
                {
                    Text = $"C{note / 12 - 1}", FontSize = 13, Foreground = _textBrush,
                    Width = ww, TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
                };
                Place(label, i * ww, h - 32);
            }
        }

        double bw = BlackWidth, bh = BlackHeight, radius = 4;
        for (int i = 0; i < _whiteKeyCount; i++)
        {
            if (!HasBlackAfter(i)) continue;
            int note = _baseNote + WhiteSemitone(i) + 1;
            // Start above the top edge (clipped): only the bottom corners are round.
            var key = new Rectangle { Width = bw, Height = bh + radius, RadiusX = radius, RadiusY = radius };
            Place(key, (i + 1) * ww - bw / 2, -radius);
            _keys[note] = (key, true);
        }

        UpdateFills();
    }

    void Place(FrameworkElement element, double left, double top)
    {
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
    }

    void UpdateFills()
    {
        var held = new Dictionary<int, int?>(); // note -> strongest velocity on it
        foreach (var t in _touches.Values)
            held[t.Note] = held.TryGetValue(t.Note, out var v) ? KeyColors.Strongest(v, t.Velocity) : t.Velocity;

        foreach (var (note, (shape, black)) in _keys)
        {
            if (held.TryGetValue(note, out var velocity))
            {
                var (r, g, b) = KeyColors.Pressed(black, velocity);
                shape.Fill = Brush(r, g, b);
            }
            else if (_marked.Contains(note))
            {
                shape.Fill = black ? _markedBlackBrush : _markedWhiteBrush;
            }
            else
            {
                shape.Fill = black ? _blackBrush : _whiteBrush;
            }
        }
    }

    // ---------------------------------------------------------------- pointer input

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
            return;

        CapturePointer(e.Pointer);
        if (HitTest(point.Position) is { } hit)
            StartTouch(e.Pointer.PointerId, hit.Note, hit.Position, ContactSize(point), inheritedVelocity: null);
        e.Handled = true;
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        uint id = e.Pointer.PointerId;
        _touches.TryGetValue(id, out var touch);
        if (touch is null && !e.Pointer.IsInContact)
            return; // mouse hovering

        var point = e.GetCurrentPoint(this);
        var hit = HitTest(point.Position);

        if (touch is not null && hit?.Note == touch.Note)
        {
            // Same key: keep collecting the contact area while the window is open.
            if (!touch.Sounding)
                touch.MaxSize = Math.Max(touch.MaxSize, ContactSize(point));
            return;
        }
        if (touch is null && hit is null)
            return;

        // Sliding to another key: new note with the same velocity, no extra delay.
        int? inherited = null;
        if (touch is not null)
        {
            EndTouch(id);
            inherited = touch.Velocity;
        }
        if (hit is { } h)
            StartTouch(id, h.Note, h.Position, ContactSize(point), inherited);
        e.Handled = true;
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndTouch(e.Pointer.PointerId);
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    void OnPointerLost(object sender, PointerRoutedEventArgs e) => EndTouch(e.Pointer.PointerId);

    // ---------------------------------------------------------------- PC keyboard

    // Piano layout from the leftmost C: A S D F G H J K L ; ' are white keys,
    // W E T Y U O P the black keys in between (semitones above BaseNote).
    static readonly Dictionary<VirtualKey, int> KeySemitones = new()
    {
        [VirtualKey.A] = 0, [VirtualKey.W] = 1, [VirtualKey.S] = 2, [VirtualKey.E] = 3, [VirtualKey.D] = 4,
        [VirtualKey.F] = 5, [VirtualKey.T] = 6, [VirtualKey.G] = 7, [VirtualKey.Y] = 8, [VirtualKey.H] = 9,
        [VirtualKey.U] = 10, [VirtualKey.J] = 11, [VirtualKey.K] = 12, [VirtualKey.O] = 13, [VirtualKey.L] = 14,
        [VirtualKey.P] = 15, [(VirtualKey)186] = 16, [(VirtualKey)222] = 17, // ; and '
    };

    const uint KeyPointerBase = 0x8000_0000; // keeps keyboard "pointers" apart from real pointer ids

    UIElement? _keyRoot;

    void AttachKeyboard()
    {
        DetachKeyboard();
        _keyRoot = XamlRoot?.Content;
        if (_keyRoot is null) return;
        // Preview (tunnelling) events: we see the key wherever focus is, before a picker or button does.
        _keyRoot.PreviewKeyDown += OnKeyDown;
        _keyRoot.PreviewKeyUp += OnKeyUp;
    }

    void DetachKeyboard()
    {
        if (_keyRoot is null) return;
        _keyRoot.PreviewKeyDown -= OnKeyDown;
        _keyRoot.PreviewKeyUp -= OnKeyUp;
        _keyRoot = null;
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Calibration measures real taps; key presses would only spoil it.
        if (_config.Mode == VelocityMode.Calibrate)
            return;

        if (e.Key is VirtualKey.PageUp or VirtualKey.PageDown)
        {
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown) // one octave per press, no auto-repeat
                OctaveShift?.Invoke(e.Key == VirtualKey.PageUp ? 1 : -1);
            return;
        }

        if (!KeySemitones.TryGetValue(e.Key, out int semitone))
            return;
        e.Handled = true; // e.g. a focused picker would otherwise jump to an item starting with that letter

        uint id = KeyPointerBase | (uint)e.Key;
        if (e.KeyStatus.WasKeyDown || _touches.ContainsKey(id))
            return; // auto-repeat

        // A key has no measurable force: play it at the fixed velocity.
        int note = _baseNote + semitone;
        if (note <= 127)
            StartTouch(id, note, position: 1, size: 0, inheritedVelocity: Math.Clamp(_config.FixedVelocity, 1, 127));
    }

    void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!KeySemitones.ContainsKey(e.Key)) return;
        e.Handled = true;
        EndTouch(KeyPointerBase | (uint)e.Key);
    }

    /// <summary>Releases notes held with the PC keyboard (their key-up goes elsewhere once the window loses focus).</summary>
    public void ReleaseKeys()
    {
        foreach (var id in _touches.Keys.Where(id => (id & KeyPointerBase) != 0).ToList())
            EndTouch(id);
    }

    void StartTouch(uint pointerId, int note, double position, double size, int? inheritedVelocity)
    {
        var touch = new ActiveTouch { Note = note, Position = position, MaxSize = size };
        _touches[pointerId] = touch;

        if (inheritedVelocity is int v)
        {
            touch.Velocity = v;
            Sound(touch);
        }
        else if (NeedsWindow)
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_config.WindowMs, 0, 100));
            timer.IsRepeating = false;
            timer.Tick += (t, _) =>
            {
                t.Stop();
                if (_touches.TryGetValue(pointerId, out var current) && ReferenceEquals(current, touch))
                    Sound(touch);
            };
            timer.Start();
        }
        else
        {
            Sound(touch);
        }
        UpdateFills();
    }

    void Sound(ActiveTouch touch)
    {
        if (touch.Sounding) return;

        touch.Velocity ??= VelocityMath.Compute(_config, touch.MaxSize, accel: 0, touch.Position);
        touch.Sounding = true;
        UpdateFills(); // key colour follows the velocity

        _noteCounts.TryGetValue(touch.Note, out int count);
        _noteCounts[touch.Note] = count + 1;
        if (count == 0)
            NoteOn?.Invoke(new NoteEventArgs(touch.Note, touch.Velocity.Value, touch.MaxSize, 0, touch.Position));
    }

    void EndTouch(uint pointerId)
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
        UpdateFills();
    }

    public void ReleaseAll()
    {
        foreach (var id in _touches.Keys.ToList())
            EndTouch(id);
    }

    static SolidColorBrush Brush(byte r, byte g, byte b) => new(WColor.FromArgb(255, r, g, b));
    static SolidColorBrush Brush((byte R, byte G, byte B) c) => Brush(c.R, c.G, c.B);
}
