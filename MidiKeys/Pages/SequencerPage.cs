using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// 16-step sequencer in the style of the Behringer 1601: black with orange lines and an orange
/// title, switches that light orange. Keeps running when you switch to another page, so you can
/// play the piano or the arp over it.
/// </summary>
public class SequencerPage : ContentPage
{
    const int MinOctave = -1;
    const int MaxOctave = 6; // top of the two-octave range stays under 127

#if ANDROID
    const string TitleFont = "serif";
#else
    const string TitleFont = "Times New Roman";
#endif

    readonly StepSequencer _seq;
    readonly SequencerPanel _panel;
    readonly List<Action> _refreshers = new();

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Label _gateLabel = Text("");
    readonly Label _octaveLabel = Text("");

    public SequencerPage(StepSequencer sequencer)
    {
        _seq = sequencer;
        Title = $"1601 SEQUENCER (ch{sequencer.Channel + 1})";
        BackgroundColor = Theme.Panel1601;

        _panel = new SequencerPanel(sequencer);
        _panel.PatternChanged += (_, _) => _seq.Save();

        // Positions arrive on the sequencer thread; hop to the UI thread for drawing.
        _seq.StepChanged += positions => Dispatcher.Dispatch(() => _panel.ShowPositions(positions));
        _seq.RunningChanged += (_, _) => Refresh();

        // ---- title bar: start/stop with the gate length next to it, back to step 1 (the tempo is in the side menu)
        _startStop.Clicked += (_, _) => { if (_seq.IsRunning) _seq.Stop(); else _seq.Start(); };
        var gate = new Slider { Minimum = 5, Maximum = 95, Value = _seq.GateLength * 100, WidthRequest = 120 };
        gate.ValueChanged += (_, e) => { _seq.GateLength = e.NewValue / 100; Refresh(); };
        gate.DragCompleted += (_, _) => _seq.Save();
        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _seq.Reset();
        PageHeader.Set(this, _startStop, Group(Theme.Print("PULSE WIDTH"), gate, _gateLabel), reset);

        // ---- the panel's switches as large buttons

        var octDown = IconButton("▼", "Octaaf omlaag");
        octDown.Clicked += (_, _) => ShiftOctave(-1);
        var octUp = IconButton("▲", "Octaaf omhoog");
        octUp.Clicked += (_, _) => ShiftOctave(1);

        var controls = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
        };
        foreach (var view in new View[]
                 {
                     new Label { Text = "1601 Sequencer", FontFamily = TitleFont, FontSize = 34, TextColor = Theme.Orange1601, VerticalOptions = LayoutOptions.Center },
                     Switch(("16/1", false), ("8/2", true), () => _seq.Split, v => _seq.Split = v),
                     Switch(("SEQUENTIAL", false), ("RANDOM", true), () => _seq.RandomOrder, v => _seq.RandomOrder = v),
                     Group(Caption("OCTAVE"), octDown, _octaveLabel, octUp),
                 })
        {
            view.Margin = new Thickness(0, 0, 24, 6);
            controls.Children.Add(view);
        }

        var grid = new Grid
        {
            Padding = 8,
            RowSpacing = 4,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(controls, 0, 0);
        grid.Add(_panel, 0, 1);
        grid.Add(_panel.Lights, 0, 1);
        Content = grid;

        Refresh();
    }

    void ShiftOctave(int delta)
    {
        int octave = Math.Clamp(_seq.BaseNote / 12 - 1 + delta, MinOctave, MaxOctave);
        _seq.BaseNote = 12 * (octave + 1);
        _seq.Save();
        Refresh();
        _panel.Refresh();
    }

    void Refresh()
    {
        _startStop.Text = _seq.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _seq.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _seq.IsRunning ? Theme.Red : Theme.Key;
        _gateLabel.Text = $"{_seq.GateLength * 100:0}%";
        int octave = _seq.BaseNote / 12 - 1;
        _octaveLabel.Text = $"C{octave}–C{octave + 2}";
        foreach (var refresh in _refreshers)
            refresh();
    }

    /// <summary>A two-way switch as two buttons side by side; the chosen side lights orange.</summary>
    View Switch((string Label, bool Value) left, (string Label, bool Value) right, Func<bool> get, Action<bool> set)
    {
        var row = new HorizontalStackLayout { Spacing = 0 };
        foreach (var (label, value) in new[] { left, right })
        {
            var button = new Button
            {
                Text = label,
                FontAttributes = FontAttributes.Bold,
                MinimumWidthRequest = 64,
                MinimumHeightRequest = 44,
                BorderColor = Theme.Orange1601,
                BorderWidth = 2,
                CornerRadius = 0,
            };
            button.Clicked += (_, _) => { set(value); _seq.Save(); Refresh(); _panel.Refresh(); };
            _refreshers.Add(() =>
            {
                bool on = get() == value;
                button.BackgroundColor = on ? Theme.Orange1601 : Theme.Panel1601;
                button.TextColor = on ? Colors.White : Theme.Silk;
            });
            row.Children.Add(button);
        }
        return row;
    }

    static View Group(params View[] views)
    {
        var stack = new HorizontalStackLayout { Spacing = 8 };
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
        var button = new Button
        {
            Text = icon,
            FontSize = 18,
            MinimumWidthRequest = 48,
            MinimumHeightRequest = 44,
            BackgroundColor = Theme.Key,
            TextColor = Theme.Silk,
        };
        ToolTipProperties.SetText(button, tip);
        return button;
    }

    static Label Caption(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        TextColor = Theme.Silk,
        VerticalOptions = LayoutOptions.Center,
    };

    static Label Text(string text) =>
        new() { Text = text, FontSize = 16, TextColor = Theme.Silk, VerticalOptions = LayoutOptions.Center };
}
