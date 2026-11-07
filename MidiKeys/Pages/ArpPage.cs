using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// Arpeggiator: every setting is a row of buttons you see at a glance and change with one tap.
/// Started, it plays while keys are down; with Hold the last chord keeps going, also on other pages.
/// </summary>
public class ArpPage : ContentPage
{
    const int MinOctave = -1;
    const int MaxOctave = 7; // two octaves plus the top C stay under 127

    static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];


    readonly Arpeggiator _arp;
    readonly List<Action> _refreshers = new();
    readonly BoxView[] _leds = new BoxView[Arpeggiator.RhythmLength];

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Button _hold = MakeButton("HOLD");
    readonly Label _gateLabel = Text("", 14, Theme.Silk);
    readonly Label _octaveLabel = Text("", 16, Theme.Silk);
    readonly Label _notesLabel = Text("", 14, Theme.SilkMid);
    readonly PianoKeyboard _keyboard = new() { WhiteKeyCount = 15 };
    readonly VelocitySettings _velocity;
    readonly EventHandler _velocityChanged;

    public ArpPage(Arpeggiator arp, VelocitySettings velocity)
    {
        _arp = arp;
        _velocity = velocity;
        Title = $"Arp (ch{arp.Channel + 1})";
        BackgroundColor = Theme.Panel;

        _startStop.Clicked += (_, _) => { if (_arp.IsRunning) _arp.Stop(); else _arp.Start(); };
        _arp.RunningChanged += (_, _) => UpdateLabels();

        // ---- in the navigation bar: start/stop and gate (the tempo is in the side menu)
        var gate = new Slider { Minimum = 5, Maximum = 95, Value = _arp.GateLength * 100, WidthRequest = 120 };
        gate.ValueChanged += (_, e) => { _arp.GateLength = e.NewValue / 100; UpdateLabels(); };
        gate.DragCompleted += (_, _) => _arp.Save();

        PageHeader.Set(this, _startStop, Group(Theme.Print("GATE"), gate, _gateLabel));

        // ---- the settings, one row of buttons each
        var settings = Wrap(
            Group(Theme.Print("MODUS"), Segments(
                [("↑", ArpMode.Up), ("↓", ArpMode.Down), ("↕", ArpMode.UpDown), ("✋", ArpMode.AsPlayed), ("🎲", ArpMode.Random)],
                () => _arp.Mode, v => _arp.Mode = v, ["Op", "Neer", "Op en neer", "Zoals gespeeld", "Willekeurig"])),
            Group(Theme.Print("OCTAVEN"), Segments(
                [("1", 1), ("2", 2), ("3", 3), ("4", 4)],
                () => _arp.Octaves, v => _arp.Octaves = v)),
            Group(Theme.Print("SNELHEID"), Segments(
                [("1/4", ArpRate.Quarter), ("1/8", ArpRate.Eighth), ("1/16", ArpRate.Sixteenth), ("1/8T", ArpRate.EighthTriplet)],
                () => _arp.Rate, v => _arp.Rate = v)),
            Group(Theme.Print("RITME"), RhythmButtons()));

        // ---- keyboard bar: hold and octave
        _hold.Clicked += (_, _) => _arp.SetHold(!_arp.Hold);
        var octDown = IconButton("▼", "Octaaf omlaag");
        octDown.Clicked += (_, _) => ShiftOctave(-1);
        var octUp = IconButton("▲", "Octaaf omhoog");
        octUp.Clicked += (_, _) => ShiftOctave(1);
        var keyboardBar = Wrap(_hold, Group(octDown, _octaveLabel, octUp), _notesLabel);

        // ---- keyboard: feeds the arp instead of sounding itself
        _keyboard.BaseNote = _arp.BaseNote;
        _keyboard.NoteOn += (_, e) => _arp.NoteOn(e.Note, e.Velocity);
        _keyboard.NoteOff += (_, note) => _arp.NoteOff(note);
        _keyboard.OctaveShiftRequested += (_, delta) => ShiftOctave(delta);
        _keyboard.VelocityConfig = velocity.Config;
        _velocityChanged = (_, _) => _keyboard.VelocityConfig = velocity.Config;
        velocity.Changed += _velocityChanged;

        _arp.NotesChanged += (_, _) => UpdateLabels();
        _arp.StepChanged += pos => Dispatcher.Dispatch(() => ShowStep(pos));

        var grid = new Grid
        {
            Padding = 8,
            RowSpacing = 4,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        grid.Add(settings, 0, 0);
        grid.Add(keyboardBar, 0, 1);
        grid.Add(_keyboard, 0, 2);
        Content = grid;

        Refresh();
    }

    /// <summary>The page is no longer used: stop listening to the shared services.</summary>
    public void Close()
    {
        _velocity.Changed -= _velocityChanged;
    }

    protected override void OnDisappearing()
    {
        // Keys held while leaving never send their release here; a held chord keeps playing.
        _arp.ReleaseKeys();
        base.OnDisappearing();
    }

    /// <summary>One button per option; the active one is lit.</summary>
    View Segments<T>((string Label, T Value)[] options, Func<T> get, Action<T> set, string[]? tips = null)
    {
        var row = new HorizontalStackLayout { Spacing = 2 };
        for (int i = 0; i < options.Length; i++)
        {
            var (label, value) = options[i];
            var button = MakeButton(label);
            if (tips is not null)
            {
                button.FontSize = 18;
                ToolTipProperties.SetText(button, tips[i]);
            }
            button.MinimumWidthRequest = 48;
            button.Clicked += (_, _) => { set(value); _arp.Save(); Refresh(); };
            _refreshers.Add(() => Theme.Light(button, EqualityComparer<T>.Default.Equals(get(), value)));
            row.Children.Add(button);
        }
        return row;
    }

    View RhythmButtons()
    {
        // An LED above every step lights up when the step plays; the steps have the 808 colours in pairs.
        var row = new HorizontalStackLayout { Spacing = 2 };
        for (int i = 0; i < _leds.Length; i++)
        {
            int step = i;
            var button = MakeButton((i + 1).ToString());
            button.WidthRequest = 44;
            button.Clicked += (_, _) => { _arp.Rhythm[step] = !_arp.Rhythm[step]; _arp.Save(); Refresh(); };
            _refreshers.Add(() => Theme.Light(button, _arp.Rhythm[step], Theme.StepColor(step, groupSize: 2)));

            _leds[i] = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4, Color = Theme.LedOff, HorizontalOptions = LayoutOptions.Center };
            row.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { _leds[i], button } });
        }
        return row;
    }

    void ShowStep(int pos)
    {
        for (int i = 0; i < _leds.Length; i++)
            _leds[i].Color = i == pos ? Theme.LedOn : Theme.LedOff;
    }

    void ShiftOctave(int delta)
    {
        int octave = Math.Clamp(_arp.BaseNote / 12 - 1 + delta, MinOctave, MaxOctave);
        _arp.BaseNote = 12 * (octave + 1);
        _keyboard.BaseNote = _arp.BaseNote;
        _arp.Save();
        UpdateLabels();
    }

    void Refresh()
    {
        foreach (var refresh in _refreshers)
            refresh();
        UpdateLabels();
    }

    void UpdateLabels()
    {
        _startStop.Text = _arp.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _arp.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _arp.IsRunning ? Theme.Red : Theme.Key;
        _gateLabel.Text = $"{_arp.GateLength * 100:0}%";
        int octave = _arp.BaseNote / 12 - 1;
        _octaveLabel.Text = $"C{octave}–C{octave + 2}";
        Theme.Light(_hold, _arp.Hold);

        var notes = _arp.Notes;
        _notesLabel.Text = !_arp.IsRunning ? "Gestopt: de toetsen spelen gewoon noten"
            : notes.Count == 0
            ? (_arp.Hold ? "Speel een akkoord: het blijft doorspelen" : "Houd toetsen ingedrukt om te arpeggiëren")
            : "Speelt: " + string.Join(" ", notes.Select(n => $"{NoteNames[n % 12]}{n / 12 - 1}"));
    }

    static FlexLayout Wrap(params View[] views)
    {
        var flex = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
        };
        foreach (var v in views)
        {
            v.Margin = new Thickness(0, 0, 16, 6);
            flex.Children.Add(v);
        }
        return flex;
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
        new() { Text = text, BackgroundColor = Theme.Key, TextColor = Theme.Silk, BorderWidth = 0 };

    static Label Text(string text, double size, Color color) =>
        new() { Text = text, FontSize = size, TextColor = color, VerticalOptions = LayoutOptions.Center };
}
