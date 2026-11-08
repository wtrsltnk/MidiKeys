using MidiKeys.Services;

namespace MidiKeys.Controls;

public sealed class NoteEventArgs(int note, int velocity, double rawSize, double rawAccel, double position) : EventArgs
{
    /// <summary>Absolute MIDI note number.</summary>
    public int Note { get; } = note;

    /// <summary>Velocity computed according to the VelocityConfig (1–127).</summary>
    public int Velocity { get; } = velocity;

    /// <summary>Largest contact diameter within the measurement window, in dp.</summary>
    public double RawSize { get; } = rawSize;

    /// <summary>Accelerometer peak around the touch-down, in m/s² (gravity filtered out).</summary>
    public double RawAccel { get; } = rawAccel;

    /// <summary>0 = top of the key, 1 = bottom.</summary>
    public double Position { get; } = position;
}

/// <summary>
/// Cross-platform facade for the keyboard. The Android handler renders a native
/// multitouch view, so chords and glissandi (sliding over keys) work.
/// </summary>
public class PianoKeyboard : View
{
    public static readonly BindableProperty BaseNoteProperty =
        BindableProperty.Create(nameof(BaseNote), typeof(int), typeof(PianoKeyboard), 48);

    public static readonly BindableProperty WhiteKeyCountProperty =
        BindableProperty.Create(nameof(WhiteKeyCount), typeof(int), typeof(PianoKeyboard), 15);

    public static readonly BindableProperty VelocityConfigProperty =
        BindableProperty.Create(nameof(VelocityConfig), typeof(VelocityConfig), typeof(PianoKeyboard), VelocityConfig.Default);

    public static readonly BindableProperty MarkedNotesProperty =
        BindableProperty.Create(nameof(MarkedNotes), typeof(int[]), typeof(PianoKeyboard), Array.Empty<int>());

    /// <summary>MIDI note of the leftmost C (48 = C3, 60 = C4).</summary>
    public int BaseNote
    {
        get => (int)GetValue(BaseNoteProperty);
        set => SetValue(BaseNoteProperty, value);
    }

    /// <summary>Number of white keys. 15 = two octaves plus the top C.</summary>
    public int WhiteKeyCount
    {
        get => (int)GetValue(WhiteKeyCountProperty);
        set => SetValue(WhiteKeyCountProperty, value);
    }

    /// <summary>Notes whose keys are shown light green (e.g. the notes the arp plays); set a new array to change them.</summary>
    public int[] MarkedNotes
    {
        get => (int[])GetValue(MarkedNotesProperty);
        set => SetValue(MarkedNotesProperty, value);
    }

    public VelocityConfig VelocityConfig
    {
        get => (VelocityConfig)GetValue(VelocityConfigProperty);
        set => SetValue(VelocityConfigProperty, value);
    }

    public event EventHandler<NoteEventArgs>? NoteOn;

    /// <summary>Absolute MIDI note number that was released.</summary>
    public event EventHandler<int>? NoteOff;

    /// <summary>+1 or −1: the user asked for another octave (Page Up / Page Down on Windows).</summary>
    public event EventHandler<int>? OctaveShiftRequested;

    internal void RaiseNoteOn(NoteEventArgs e) => NoteOn?.Invoke(this, e);
    internal void RaiseNoteOff(int note) => NoteOff?.Invoke(this, note);
    internal void RaiseOctaveShift(int delta) => OctaveShiftRequested?.Invoke(this, delta);
}
