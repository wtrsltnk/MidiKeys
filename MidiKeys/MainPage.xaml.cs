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

    List<MidiOutputDevice> _devices = [];
    bool _updatingPicker;
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
        Keyboard.VelocityConfig = _velocity.Config;
        _velocity.Changed += (_, _) => Keyboard.VelocityConfig = _velocity.Config;

        _midi.DevicesChanged += (_, _) => RefreshDevices(autoConnect: true);
        _midi.ConnectionChanged += (_, _) => { _soundingNotes.Clear(); UpdateStatus(); };

        UpdateOctave();
        RefreshDevices(autoConnect: true);
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

    async void OnDeviceSelected(object? sender, EventArgs e)
    {
        if (_updatingPicker) return;
        int index = DevicePicker.SelectedIndex;
        if (index < 0 || index >= _devices.Count) return;

        StatusLabel.Text = "Verbinden…";
        if (!await _midi.ConnectAsync(_devices[index].Id))
            StatusLabel.Text = "Verbinden mislukt";
    }

    async void RefreshDevices(bool autoConnect)
    {
        _devices = _midi.GetDevices().ToList();

        _updatingPicker = true;
        DevicePicker.ItemsSource = _devices.Select(d => d.Name).ToList();
        DevicePicker.SelectedIndex = _devices.FindIndex(d => d.Id == _midi.ConnectedDeviceId);
        _updatingPicker = false;

        UpdateStatus();

        if (autoConnect && !_midi.IsConnected && _devices.Count > 0)
            await _midi.ConnectAsync(_devices[0].Id);
    }

    void UpdateStatus()
    {
        if (!_midi.IsSupported)
            StatusLabel.Text = "Dit apparaat ondersteunt geen MIDI";
        else if (_midi.IsConnected)
            StatusLabel.Text = $"● {_midi.ConnectedDeviceName}";
        else if (_devices.Count == 0)
            StatusLabel.Text = "Sluit een USB-MIDI-kabel aan";
        else
            StatusLabel.Text = "Niet verbonden";

        _updatingPicker = true;
        DevicePicker.SelectedIndex = _devices.FindIndex(d => d.Id == _midi.ConnectedDeviceId);
        _updatingPicker = false;
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
