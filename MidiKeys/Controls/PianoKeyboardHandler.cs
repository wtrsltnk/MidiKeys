using Microsoft.Maui.Handlers;

namespace MidiKeys.Controls;

public class PianoKeyboardHandler : ViewHandler<PianoKeyboard, PianoKeyboardView>
{
    public static readonly IPropertyMapper<PianoKeyboard, PianoKeyboardHandler> Mapper =
        new PropertyMapper<PianoKeyboard, PianoKeyboardHandler>(ViewMapper)
        {
            [nameof(PianoKeyboard.BaseNote)] = (h, v) => h.PlatformView.BaseNote = v.BaseNote,
            [nameof(PianoKeyboard.WhiteKeyCount)] = (h, v) => h.PlatformView.WhiteKeyCount = v.WhiteKeyCount,
            [nameof(PianoKeyboard.VelocityConfig)] = (h, v) => h.PlatformView.VelocityConfig = v.VelocityConfig,
        };

    public PianoKeyboardHandler() : base(Mapper) { }

#if ANDROID
    protected override PianoKeyboardView CreatePlatformView() => new(Context);
#else
    protected override PianoKeyboardView CreatePlatformView() => new();
#endif

    protected override void ConnectHandler(PianoKeyboardView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.NoteOn += OnNoteOn;
        platformView.NoteOff += OnNoteOff;
#if WINDOWS
        VirtualView.Loaded += OnLoaded;
        VirtualView.Unloaded += OnUnloaded;
#endif
    }

    protected override void DisconnectHandler(PianoKeyboardView platformView)
    {
        platformView.ReleaseAll();
        platformView.NoteOn -= OnNoteOn;
        platformView.NoteOff -= OnNoteOff;
#if WINDOWS
        VirtualView.Loaded -= OnLoaded;
        VirtualView.Unloaded -= OnUnloaded;
        OnUnloaded(null, EventArgs.Empty);
#endif
        base.DisconnectHandler(platformView);
    }

    void OnNoteOn(NoteEventArgs e) => VirtualView?.RaiseNoteOn(e);
    void OnNoteOff(int note) => VirtualView?.RaiseNoteOff(note);

#if WINDOWS
    Microsoft.Maui.Controls.Window? _window;

    void OnLoaded(object? sender, EventArgs e)
    {
        OnUnloaded(sender, e);
        _window = VirtualView?.Window;
        if (_window is not null)
            _window.Deactivated += OnWindowDeactivated;
    }

    void OnUnloaded(object? sender, EventArgs e)
    {
        if (_window is not null)
            _window.Deactivated -= OnWindowDeactivated;
        _window = null;
    }

    // A key released while another app has focus never reaches us: let go of keyboard notes.
    void OnWindowDeactivated(object? sender, EventArgs e) => PlatformView?.ReleaseKeys();
#endif
}
