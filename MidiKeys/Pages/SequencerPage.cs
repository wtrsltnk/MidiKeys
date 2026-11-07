using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// 16-step sequencer in the style of the Behringer / ARP 1601. Keeps running when you switch
/// to another page, so you can play the piano over it.
/// </summary>
public class SequencerPage : ContentPage
{
    const int MinOctave = -1;
    const int MaxOctave = 6; // top of the two-octave range stays under 127

    readonly StepSequencer _seq;
    readonly IMidiOutput _midi;
    readonly SequencerPanel _panel;

    readonly Button _startStop = MakeButton("▶ Start");
    readonly Button _split = MakeButton("");
    readonly Button _order = MakeButton("");
    readonly Label _tempoLabel = Text("", 14, "#FFFFFF");
    readonly Label _gateLabel = Text("", 14, "#FFFFFF");
    readonly Label _octaveLabel = Text("", 16, "#FFFFFF");
    readonly Label _status = Text("", 13, "#A6ADC8");
    readonly Picker _channelA = MakeChannelPicker("Kanaal A");
    readonly Picker _channelB = MakeChannelPicker("Kanaal B");

    public SequencerPage(StepSequencer sequencer, IMidiOutput midi)
    {
        _seq = sequencer;
        _midi = midi;
        Title = "Sequencer";
        BackgroundColor = Color.FromArgb("#1E1E2E");

        _panel = new SequencerPanel(sequencer);
        _panel.PatternChanged += (_, _) => _seq.Save();

        // Positions arrive on the sequencer thread; hop to the UI thread for drawing.
        _seq.StepChanged += positions => Dispatcher.Dispatch(() => _panel.ShowPositions(positions));
        _seq.RunningChanged += (_, _) => UpdateLabels();
        _midi.ConnectionChanged += (_, _) => UpdateLabels();

        _startStop.Clicked += (_, _) => { if (_seq.IsRunning) _seq.Stop(); else _seq.Start(); };

        var reset = MakeButton("⏮ Reset");
        reset.Clicked += (_, _) => _seq.Reset();

        var tempo = new Slider { Minimum = 30, Maximum = 300, Value = _seq.Bpm, WidthRequest = 160 };
        tempo.ValueChanged += (_, e) => { _seq.Bpm = (int)Math.Round(e.NewValue); UpdateLabels(); };
        tempo.DragCompleted += (_, _) => _seq.Save();

        var gate = new Slider { Minimum = 5, Maximum = 95, Value = _seq.GateLength * 100, WidthRequest = 120 };
        gate.ValueChanged += (_, e) => { _seq.GateLength = e.NewValue / 100; UpdateLabels(); };
        gate.DragCompleted += (_, _) => _seq.Save();

        var octDown = MakeButton("Oct −");
        octDown.Clicked += (_, _) => ShiftOctave(-1);
        var octUp = MakeButton("Oct +");
        octUp.Clicked += (_, _) => ShiftOctave(1);

        _split.Clicked += (_, _) => { _seq.Split = !_seq.Split; Changed(); };
        _order.Clicked += (_, _) => { _seq.RandomOrder = !_seq.RandomOrder; Changed(); };

        _channelA.SelectedIndex = _seq.ChannelA;
        _channelB.SelectedIndex = _seq.ChannelB;
        _channelA.SelectedIndexChanged += (_, _) => { if (_channelA.SelectedIndex >= 0) { _seq.ChannelA = _channelA.SelectedIndex; _seq.Save(); } };
        _channelB.SelectedIndexChanged += (_, _) => { if (_channelB.SelectedIndex >= 0) { _seq.ChannelB = _channelB.SelectedIndex; _seq.Save(); } };

        var toolbar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children =
            {
                _startStop, reset,
                Group(Text("TEMPO", 12, "#A6ADC8"), tempo, _tempoLabel),
                Group(Text("GATE", 12, "#A6ADC8"), gate, _gateLabel),
                Group(octDown, _octaveLabel, octUp),
                _split, _order, _channelA, _channelB, _status,
            },
        };
        foreach (var child in toolbar.Children.OfType<View>())
            child.Margin = new Thickness(0, 0, 8, 6);

        var grid = new Grid
        {
            Padding = 8,
            RowSpacing = 4,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(toolbar, 0, 0);
        grid.Add(_panel, 0, 1);
        Content = grid;

        UpdateLabels();
    }

    void ShiftOctave(int delta)
    {
        int octave = Math.Clamp(_seq.BaseNote / 12 - 1 + delta, MinOctave, MaxOctave);
        _seq.BaseNote = 12 * (octave + 1);
        Changed();
    }

    void Changed()
    {
        _seq.Save();
        UpdateLabels();
        _panel.Invalidate();
    }

    void UpdateLabels()
    {
        _startStop.Text = _seq.IsRunning ? "■ Stop" : "▶ Start";
        _startStop.BackgroundColor = Color.FromArgb(_seq.IsRunning ? "#E5483A" : "#313244");
        _tempoLabel.Text = $"{_seq.Bpm} BPM";
        _gateLabel.Text = $"{_seq.GateLength * 100:0}%";
        int octave = _seq.BaseNote / 12 - 1;
        _octaveLabel.Text = $"C{octave}–C{octave + 2}";
        _split.Text = _seq.Split ? "2 × 8 stappen" : "16 stappen";
        _order.Text = _seq.RandomOrder ? "Volgorde: willekeurig" : "Volgorde: vooruit";
        _channelA.Title = _seq.Split ? "Kanaal A" : "Kanaal";
        _channelB.IsVisible = _seq.Split;
        _status.Text = _midi.IsConnected ? $"● {_midi.ConnectedDeviceName}" : "Geen MIDI-apparaat (kies er een op Piano)";
    }

    static View Group(params View[] views)
    {
        var stack = new HorizontalStackLayout { Spacing = 6 };
        foreach (var v in views)
        {
            v.VerticalOptions = LayoutOptions.Center;
            stack.Children.Add(v);
        }
        return stack;
    }

    static Button MakeButton(string text) =>
        new() { Text = text, BackgroundColor = Color.FromArgb("#313244"), TextColor = Colors.White };

    static Picker MakeChannelPicker(string title) => new()
    {
        Title = title,
        WidthRequest = 90,
        TextColor = Colors.White,
        TitleColor = Color.FromArgb("#A6ADC8"),
        ItemsSource = Enumerable.Range(1, 16).Select(i => i.ToString()).ToList(),
    };

    static Label Text(string text, double size, string color) =>
        new() { Text = text, FontSize = size, TextColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Center };
}
