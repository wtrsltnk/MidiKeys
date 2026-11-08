using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// The round 8-step sequencer of a Dato DUO. Stopped, tap the keys to enter a melody step by step;
/// running, a key records into the step that plays. Keeps running when you switch to another page.
/// </summary>
public class DuoPage : ContentPage
{
    static readonly Color Lit = Color.FromArgb("#ECE9E2");
    static readonly Color Unlit = Color.FromArgb("#2E2E2E");

    readonly DuoSequencer _duo;
    readonly DuoPanel _panel;
    readonly List<Action> _refreshers = new();

    readonly Button _startStop = IconButton("▶", "Start");

    public DuoPage(DuoSequencer duo, MusicClock clock)
    {
        _duo = duo;
        Title = $"DUO (ch{duo.Channel + 1})";
        BackgroundColor = Color.FromArgb("#141414");

        _panel = new DuoPanel(duo);
        _panel.Changed += (_, _) => _duo.Save();
        _panel.PlayPressed += (_, _) => StartStop();
        _panel.KeyPressed += (_, key) => EnterKey(key);
        _panel.OctavePressed += (_, delta) => ShiftOctave(delta);

        // Positions arrive on the clock thread; hop to the UI thread for drawing.
        _duo.StepChanged += step => Dispatcher.Dispatch(() => _panel.ShowPosition(step));
        _duo.RunningChanged += (_, _) => Refresh();

        // ---- title bar: start/stop, then tempo and note length (a tap opens a popup to change them), back to step 1
        _startStop.Clicked += (_, _) => StartStop();
        var gate = ValueButton.Percent("LENGTH", 0.05, 0.95, () => _duo.Gate, v => _duo.Gate = v);
        gate.Done += (_, _) => _duo.Save();
        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _duo.Reset();
        PageHeader.Set(this, _startStop, ValueButton.Tempo(clock), gate, reset);

        // ---- speed, random, boost
        var speed = new HorizontalStackLayout { Spacing = 2 };
        foreach (var (label, rate) in new[] { ("1/4", 0), ("1/8", 1), ("1/16", 2) })
        {
            var button = Key(label);
            button.Clicked += (_, _) => { _duo.Rate = rate; Changed(); };
            _refreshers.Add(() => Light(button, _duo.Rate == rate));
            speed.Children.Add(button);
        }

        var random = Key("RANDOM");
        ToolTipProperties.SetText(random, "De stappen in willekeurige volgorde");
        random.Clicked += (_, _) => { _duo.RandomOrder = !_duo.RandomOrder; Changed(); };
        _refreshers.Add(() => Light(random, _duo.RandomOrder));

        var boost = Key("BOOST");
        ToolTipProperties.SetText(boost, "Alle noten met volle aanslag");
        boost.Clicked += (_, _) => { _duo.Boost = !_duo.Boost; Changed(); };
        _refreshers.Add(() => Light(boost, _duo.Boost));

        var controls = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (var view in new View[]
                 {
                     new Label { Text = "duo", FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = Lit, VerticalOptions = LayoutOptions.Center },
                     Group(Caption("SPEED"), speed),
                     Group(random, boost),
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

    void StartStop()
    {
        if (_duo.IsRunning) _duo.Stop(); else _duo.Start();
    }

    /// <summary>Running: record into the step that plays. Stopped: set the chosen step, hear it, move on.</summary>
    void EnterKey(int key)
    {
        int step = _duo.IsRunning ? _duo.Position : _panel.EditStep;
        if (step < 0) step = 0;
        _duo.Keys[step] = key;
        _duo.On[step] = true;
        if (!_duo.IsRunning)
        {
            _duo.Preview(key);
            _panel.EditStep = (step + 1) % DuoSequencer.StepCount;
        }
        _duo.Save();
        _panel.Refresh();
    }

    /// <summary>The red arrows on the panel: the whole keyboard an octave down or up.</summary>
    void ShiftOctave(int delta)
    {
        _duo.Root = Math.Clamp(_duo.Root + 12 * delta, 24, 96);
        Changed();
    }

    void Changed()
    {
        _duo.Save();
        Refresh();
    }

    void Refresh()
    {
        _startStop.Text = _duo.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _duo.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _duo.IsRunning ? Theme.Red : Theme.Key;
        foreach (var refresh in _refreshers)
            refresh();
        _panel.Refresh();
    }

    static void Light(Button button, bool on)
    {
        button.BackgroundColor = on ? Lit : Unlit;
        button.TextColor = on ? Colors.Black : Theme.Silk;
    }

    static Button Key(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        MinimumHeightRequest = 44,
        MinimumWidthRequest = 60,
        Padding = new Thickness(8, 0),
        BackgroundColor = Unlit,
        TextColor = Theme.Silk,
    };

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
}
