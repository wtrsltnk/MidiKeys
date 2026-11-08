using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// Choose the MIDI output device. Connected, the picker shows only the device; otherwise its title
/// says why not (no cable, connecting, failed). Connects to the first device by itself when
/// nothing is connected and one gets plugged in.
/// </summary>
public class MidiDevicePicker : VerticalStackLayout
{
    readonly IMidiOutput _midi;
    readonly Picker _picker = new();

    List<MidiOutputDevice> _devices = [];
    bool _updating;

    public MidiDevicePicker(IMidiOutput midi)
    {
        _midi = midi;
        Spacing = 4;
        // Same heading as the other side-menu sections; the picker itself has no title.
        Children.Add(new Label
        {
            Text = "MIDI-APPARAAT",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Orange,
        });
        Children.Add(_picker);

        _picker.SelectedIndexChanged += OnSelected;
        _midi.DevicesChanged += (_, _) => Refresh(autoConnect: true);
        _midi.ConnectionChanged += (_, _) => UpdateStatus();

        Refresh(autoConnect: true);
    }

    async void OnSelected(object? sender, EventArgs e)
    {
        if (_updating) return;
        int index = _picker.SelectedIndex;
        if (index < 0 || index >= _devices.Count) return;

        _picker.Title = "Verbinden…";
        if (!await _midi.ConnectAsync(_devices[index].Id))
        {
            _picker.Title = "Verbinden mislukt";
            _updating = true;
            _picker.SelectedIndex = -1; // show the message
            _updating = false;
        }
    }

    async void Refresh(bool autoConnect)
    {
        _devices = _midi.GetDevices().ToList();

        _updating = true;
        _picker.ItemsSource = _devices.Select(d => d.Name).ToList();
        _updating = false;

        UpdateStatus();

        if (autoConnect && !_midi.IsConnected && _devices.Count > 0)
            await _midi.ConnectAsync(_devices[0].Id);
    }

    void UpdateStatus()
    {
        // Only a reason while not connected; connected, the picker shows the device and nothing else.
        // (On Windows the picker shows its title above the list.)
        if (!_midi.IsSupported)
            _picker.Title = "Dit apparaat ondersteunt geen MIDI";
        else if (_midi.IsConnected)
            _picker.Title = null;
        else if (_devices.Count == 0)
            _picker.Title = "Sluit een USB-MIDI-kabel aan";
        else
            _picker.Title = "Niet verbonden";

        _updating = true;
        _picker.SelectedIndex = _devices.FindIndex(d => d.Id == _midi.ConnectedDeviceId);
        _updating = false;
    }
}
