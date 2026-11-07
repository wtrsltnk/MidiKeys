using MidiKeys.Controls;
using MidiKeys.Pages;
using MidiKeys.Services;

namespace MidiKeys;

public partial class MainPage : ContentPage
{
    const int MinOctave = -1; // C-1 = MIDI note 0
    const int MaxOctave = 7;  // C7..C9 still fits under 127 with 2 octaves + C

    readonly IMidiOutput _midi;
    readonly VelocitySettings _velocity;
    readonly Dictionary<int, int> _soundingNotes = new(); // note -> channel it was sent on

    int _octave = 3;
    int _channel;

    public MainPage(IMidiOutput midi, VelocitySettings velocity)
    {
        InitializeComponent();
        _midi = midi;
        _velocity = velocity;

        ChannelPicker.ItemsSource = Enumerable.Range(1, 16).Select(i => i.ToString()).ToList();
        ChannelPicker.SelectedIndex = 0;

        Keyboard.NoteOn += OnKeyboardNoteOn;
        Keyboard.NoteOff += OnKeyboardNoteOff;
        Keyboard.OctaveShiftRequested += (_, delta) => { if (delta > 0) OnOctaveUp(this, EventArgs.Empty); else OnOctaveDown(this, EventArgs.Empty); };
        Keyboard.VelocityConfig = _velocity.Config;
        _velocity.Changed += (_, _) => Keyboard.VelocityConfig = _velocity.Config;

        _midi.ConnectionChanged += (_, _) => _soundingNotes.Clear();

        UpdateOctave();
    }

    void OnKeyboardNoteOn(object? sender, NoteEventArgs e)
    {
        _soundingNotes[e.Note] = _channel;
        _midi.NoteOn(_channel, e.Note, e.Velocity);
    }

    void OnKeyboardNoteOff(object? sender, int note)
    {
        // Send note-off on the channel the note-on went out on, even if the channel changed meanwhile.
        int channel = _soundingNotes.Remove(note, out var ch) ? ch : _channel;
        _midi.NoteOff(channel, note);
    }

    async void OnVelocitySettings(object? sender, EventArgs e) =>
        await Navigation.PushModalAsync(new SettingsPage(_velocity));

    void OnOctaveDown(object? sender, EventArgs e) { _octave = Math.Max(MinOctave, _octave - 1); UpdateOctave(); }
    void OnOctaveUp(object? sender, EventArgs e) { _octave = Math.Min(MaxOctave, _octave + 1); UpdateOctave(); }

    void UpdateOctave()
    {
        Keyboard.BaseNote = 12 * (_octave + 1);
        OctaveLabel.Text = $"C{_octave}–C{_octave + 2}";
    }

    void OnChannelSelected(object? sender, EventArgs e)
    {
        if (ChannelPicker.SelectedIndex >= 0)
            _channel = ChannelPicker.SelectedIndex;
    }

    protected override void OnDisappearing()
    {
        // Avoid hanging notes when the app goes to the background or a settings page opens.
        foreach (var (note, channel) in _soundingNotes)
            _midi.NoteOff(channel, note);
        _soundingNotes.Clear();
        _midi.AllNotesOff(_channel);
        base.OnDisappearing();
    }
}
