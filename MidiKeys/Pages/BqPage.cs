using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// 3 × 8-step sequencer in the style of the Behringer BQ-10. Keeps running when you switch to
/// another page, so you can play the piano or the arp over it.
/// </summary>
public class BqPage : ContentPage
{
    const int MinOctave = -1;
    const int MaxOctave = 7;

    static readonly Color Lit = Color.FromArgb("#E9E9E7");
    static readonly Color Unlit = Color.FromArgb("#2E2E2E");

    // The SEQUENCE MODE positions as named in the quick start guide, with a short label for the button.
    static readonly (BqMode Mode, string Label, string Name)[] Modes =
    [
        (BqMode.LoopReverseStepped, "REV STEP", "Loop reverse stepped: A, dan B achterstevoren; alleen met STEP"),
        (BqMode.ContinuousReverse, "REV LOOP", "Continuous reverse: A, dan B achterstevoren, steeds door"),
        (BqMode.ThreeChannel, "3 CH", "Three channel: A, B en C lopen naast elkaar"),
        (BqMode.SingleStepped, "A/B STEP", "A/B single stepped: A, dan B; alleen met STEP"),
        (BqMode.Continuous, "A/B LOOP", "A/B continuous: A, dan B, steeds door"),
        (BqMode.Single, "A/B ONCE", "A/B single: A, dan B, dan stopt hij"),
        (BqMode.Alternate, "ALTERNATE", "A/B alternate: A1, B1, A2, B2 …"),
        (BqMode.Random, "RANDOM", "Random: willekeurige stappen uit A en B"),
    ];

    readonly BqSequencer _bq;
    readonly BqPanel _panel;
    readonly List<Action> _refreshers = new();

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Button _step = IconButton("STEP", "Eén stap verder (als hij stilstaat)");
    readonly Label[] _portaLabels = [Text(""), Text("")];
    readonly Grid _modes = new() { ColumnSpacing = 4, RowSpacing = 4 };
    readonly List<Button> _modeButtons = new();
    readonly Label _octaveLabel = Text("");

    public BqPage(BqSequencer bq, MusicClock clock)
    {
        _bq = bq;
        Title = $"BQ-10 (ch{bq.Channel + 1})";
        BackgroundColor = Theme.Panel1601;

        _panel = new BqPanel(bq);
        _panel.Changed += (_, _) => _bq.Save();

        // Positions arrive on the clock thread; hop to the UI thread for drawing.
        _bq.StepChanged += position => Dispatcher.Dispatch(() => _panel.ShowPosition(position));
        _bq.RunningChanged += (_, _) => Refresh();

        // ---- title bar: start/stop, then tempo and duty (a tap opens a popup to change them), step and back to step 1
        _startStop.Clicked += (_, _) => { if (_bq.IsRunning) _bq.Stop(); else _bq.Start(); };
        _step.FontSize = 14;
        _step.FontAttributes = FontAttributes.Bold;
        _step.Clicked += (_, _) => _bq.Step();
        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _bq.Reset();
        var duty = ValueButton.Percent("DUTY", 0.1, 0.9, () => _bq.Duty, v => _bq.Duty = v);
        duty.Done += (_, _) => _bq.Save();
        PageHeader.Set(this, _startStop, ValueButton.Tempo(clock), duty, _step, reset);

        // ---- sequence mode: eight buttons of equal width, in one row (or two on a narrow screen)
        foreach (var (mode, label, name) in Modes)
        {
            var button = new Button { Text = label, FontAttributes = FontAttributes.Bold, MinimumHeightRequest = 44, Padding = new Thickness(4, 0) };
            ToolTipProperties.SetText(button, name);
            button.Clicked += (_, _) =>
            {
                _bq.Mode = mode;
                if (_bq.IsStepped) _bq.Stop(); // the stepped modes only move with STEP
                _bq.Save();
                Refresh();
            };
            _refreshers.Add(() =>
            {
                bool on = _bq.Mode == mode;
                button.BackgroundColor = on ? Lit : Unlit;
                button.TextColor = on ? Colors.Black : Theme.Silk;
            });
            _modeButtons.Add(button);
        }
        ArrangeModes(8);
        SizeChanged += (_, _) => ArrangeModes(Width >= 1100 || Width <= 0 ? 8 : 4);

        var header = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
        };
        header.Add(new Label { Text = "BQ-10", FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = Theme.Silk, VerticalOptions = LayoutOptions.Center }, 0, 0);
        header.Add(Caption("SEQUENCE MODE"), 1, 0);
        header.Add(_modes, 2, 0);

        // ---- portamento and octave: caption above, control below, in equal columns
        var octDown = IconButton("▼", "Octaaf omlaag");
        octDown.Clicked += (_, _) => ShiftOctave(-1);
        var octUp = IconButton("▲", "Octaaf omhoog");
        octUp.Clicked += (_, _) => ShiftOctave(1);

        var settings = new Grid
        {
            ColumnSpacing = 24,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        settings.Add(Cell("PORTAMENTO A", Setting(() => _bq.Portamento[0], v => _bq.Portamento[0] = v), _portaLabels[0]), 0, 0);
        settings.Add(Cell("PORTAMENTO B", Setting(() => _bq.Portamento[1], v => _bq.Portamento[1] = v), _portaLabels[1]), 1, 0);
        settings.Add(Cell("OCTAVE", Group(octDown, _octaveLabel, octUp)), 2, 0);

        var controls = new VerticalStackLayout { Spacing = 8, Children = { header, settings } };

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
        int octave = Math.Clamp(_bq.BaseNote / 12 - 1 + delta, MinOctave, MaxOctave);
        _bq.BaseNote = 12 * (octave + 1);
        _bq.Save();
        Refresh();
        _panel.Invalidate();
    }

    void Refresh()
    {
        _startStop.Text = _bq.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _bq.IsStepped ? "Deze stand loopt alleen met STEP" : _bq.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _bq.IsRunning ? Theme.Red : Theme.Key;
        _startStop.IsEnabled = !_bq.IsStepped;
        _step.IsEnabled = !_bq.IsRunning;
        for (int row = 0; row < 2; row++)
            _portaLabels[row].Text = $"{_bq.Portamento[row] * 100:0}%";
        _octaveLabel.Text = $"C{_bq.BaseNote / 12 - 1}";
        foreach (var refresh in _refreshers)
            refresh();
    }

    /// <summary>The mode buttons in equal columns: all eight in one row, or two rows of four.</summary>
    void ArrangeModes(int columns)
    {
        if (_modes.ColumnDefinitions.Count == columns) return;
        _modes.Children.Clear();
        _modes.ColumnDefinitions.Clear();
        _modes.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++)
            _modes.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < _modeButtons.Count / columns; r++)
            _modes.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < _modeButtons.Count; i++)
            _modes.Add(_modeButtons[i], i % columns, i / columns);
    }

    /// <summary>A caption with its control below it; an optional value on the right of the control.</summary>
    static View Cell(string caption, View control, Label? value = null)
    {
        var cell = new VerticalStackLayout { Spacing = 2, Children = { Caption(caption) } };
        if (value is null)
        {
            cell.Children.Add(control);
            return cell;
        }

        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(48)) },
        };
        row.Add(control, 0, 0);
        row.Add(value, 1, 0);
        cell.Children.Add(row);
        return cell;
    }

    /// <summary>A 0–1 setting as a slider in the panel's white-on-grey style.</summary>
    Slider Setting(Func<double> get, Action<double> set)
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = get(),
            MinimumTrackColor = Theme.Silk,
            MaximumTrackColor = Unlit,
            ThumbColor = Lit,
        };
        slider.ValueChanged += (_, e) => { set(e.NewValue); Refresh(); };
        slider.DragCompleted += (_, _) => _bq.Save();
        return slider;
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
