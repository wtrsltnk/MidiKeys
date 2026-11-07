using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// 16-step sequencer in the style of the Behringer / ARP 1601. Keeps running when you switch
/// to another page, so you can play the piano or the arp over it.
/// </summary>
public class SequencerPage : ContentPage
{
    const int MinOctave = -1;
    const int MaxOctave = 6; // top of the two-octave range stays under 127

    readonly StepSequencer _seq;
    readonly SequencerPanel _panel;

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Button _split = IconButton("", "");
    readonly Button _order = IconButton("", "");
    readonly Label _gateLabel = Text("", 14, Theme.Silk);
    readonly Label _octaveLabel = Text("", 16, Theme.Silk);

    public SequencerPage(StepSequencer sequencer)
    {
        _seq = sequencer;
        Title = $"Sequencer (ch{sequencer.Channel + 1})";
        BackgroundColor = Theme.Panel;

        _panel = new SequencerPanel(sequencer);
        _panel.PatternChanged += (_, _) => _seq.Save();

        // Positions arrive on the sequencer thread; hop to the UI thread for drawing.
        _seq.StepChanged += positions => Dispatcher.Dispatch(() => _panel.ShowPositions(positions));
        _seq.RunningChanged += (_, _) => UpdateLabels();

        _startStop.Clicked += (_, _) => { if (_seq.IsRunning) _seq.Stop(); else _seq.Start(); };

        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _seq.Reset();

        var gate = new Slider { Minimum = 5, Maximum = 95, Value = _seq.GateLength * 100, WidthRequest = 120 };
        gate.ValueChanged += (_, e) => { _seq.GateLength = e.NewValue / 100; UpdateLabels(); };
        gate.DragCompleted += (_, _) => _seq.Save();

        var octDown = IconButton("▼", "Octaaf omlaag");
        octDown.Clicked += (_, _) => ShiftOctave(-1);
        var octUp = IconButton("▲", "Octaaf omhoog");
        octUp.Clicked += (_, _) => ShiftOctave(1);

        _split.Clicked += (_, _) => { _seq.Split = !_seq.Split; Changed(); };
        _order.Clicked += (_, _) => { _seq.RandomOrder = !_seq.RandomOrder; Changed(); };

        PageHeader.Set(this, _startStop, reset, Group(Theme.Print("GATE"), gate, _gateLabel));

        var toolbar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children =
            {
                Group(octDown, _octaveLabel, octUp),
                _split, _order,
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
        _startStop.Text = _seq.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _seq.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _seq.IsRunning ? Theme.Red : Theme.Key;
        _gateLabel.Text = $"{_seq.GateLength * 100:0}%";
        int octave = _seq.BaseNote / 12 - 1;
        _octaveLabel.Text = $"C{octave}–C{octave + 2}";
        _split.Text = _seq.Split ? "2×8" : "1×16";
        ToolTipProperties.SetText(_split, _seq.Split ? "Twee sequences van 8 stappen" : "Eén sequence van 16 stappen");
        _order.Text = _seq.RandomOrder ? "🔀" : "➡";
        ToolTipProperties.SetText(_order, _seq.RandomOrder ? "Volgorde: willekeurig" : "Volgorde: vooruit");
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

    /// <summary>A button with only an icon; the tooltip (hover, or long-press on Android) says what it does.</summary>
    static Button IconButton(string icon, string tip)
    {
        var button = MakeButton(icon);
        button.FontSize = 18;
        button.MinimumWidthRequest = 44;
        ToolTipProperties.SetText(button, tip);
        return button;
    }

    static Button MakeButton(string text) =>
        new() { Text = text, BackgroundColor = Theme.Key, TextColor = Theme.Silk };

    static Label Text(string text, double size, Color color) =>
        new() { Text = text, FontSize = size, TextColor = color, VerticalOptions = LayoutOptions.Center };
}
