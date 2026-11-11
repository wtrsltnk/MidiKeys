using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// A sequence of an Arturia BeatStep Pro: a melodic one (SEQ) or the drum sequence (DRUM). Drag the
/// encoders, switch steps with the step buttons, choose the page of 16 steps. One bar holds what you
/// need while playing; the settings are under ⚙. Keeps running when you switch to another page.
/// </summary>
public class BeatStepPage : ContentPage
{
    static readonly Color Lit = Color.FromArgb("#3BA8E8");
    static readonly Color Unlit = Color.FromArgb("#2E2F32");

    readonly BeatStepSequence _seq;
    readonly BeatStepDrum? _drum;
    readonly BeatStepPanel _panel;
    readonly List<Action> _refreshers = new();

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Label _transposeLabel = Text("");
    readonly Label _noteLabel = Text("");
    readonly Grid _setup = new()
    {
        ColumnSpacing = 16,
        RowSpacing = 10,
        ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
    };
    bool _follow;
    bool _muteMode;

    public BeatStepPage(BeatStepSequence seq, MusicClock clock)
    {
        _seq = seq;
        _drum = seq as BeatStepDrum;
        BackgroundColor = Color.FromArgb("#101112");

        _panel = new BeatStepPanel(seq);
        _panel.Changed += (_, _) => { _seq.Save(); Refresh(); };
        _panel.LastSet += (_, _) => Refresh();
        _panel.PadPressed += (_, pad) => PressPad(pad);

        // Positions arrive on the clock thread; hop to the UI thread for drawing.
        _seq.StepChanged += step => Dispatcher.Dispatch(() =>
        {
            int page = step / BeatStepSequence.PageSize;
            if (_follow && step >= 0 && page != _panel.Page)
            {
                _panel.Page = page;
                Refresh();
            }
            _panel.ShowPosition(step);
        });
        _seq.RunningChanged += (_, _) => Refresh();

        // ---- title bar: start/stop, tempo (a tap opens a popup to change it), back to step 1
        _startStop.Clicked += (_, _) => { if (_seq.IsRunning) _seq.Stop(); else _seq.Start(); };
        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _seq.Reset();
        PageHeader.Set(this, _startStop, ValueButton.Tempo(clock), reset);

        // ---- one bar with what you need while playing; the rest is under ⚙ (SHIFT + pads on the hardware)
        var bar = new HorizontalStackLayout { Spacing = 12 };

        if (_drum is not null)
        {
            var rows = Tight();
            for (int i = 0; i < _drum.RowNames.Length; i++)
            {
                int row = i;
                var button = Key(_drum.RowNames[i]);
                ToolTipProperties.SetText(button, "Wat de encoders van deze pad instellen");
                button.Clicked += (_, _) => { _panel.Row = row; Refresh(); };
                _refreshers.Add(() => Light(button, _panel.Row == row));
                rows.Children.Add(button);
            }
            bar.Children.Add(rows);
        }

        var pages = Tight();
        for (int i = 0; i < BeatStepSequence.PageCount; i++)
        {
            int page = i;
            var button = Key($"P{i + 1}");
            button.MinimumWidthRequest = 44;
            ToolTipProperties.SetText(button, $"Pagina {i + 1}: stap {i * BeatStepSequence.PageSize + 1}–{(i + 1) * BeatStepSequence.PageSize}");
            button.Clicked += (_, _) => { _panel.Page = page; _follow = false; Refresh(); };
            _refreshers.Add(() =>
            {
                bool used = page * BeatStepSequence.PageSize < _seq.Length;
                Light(button, _panel.Page == page);
                if (!used && _panel.Page != page)
                    button.TextColor = Theme.SilkDim;
            });
            pages.Children.Add(button);
        }
        var follow = Key("FOLLOW");
        ToolTipProperties.SetText(follow, "De weergave volgt de pagina die speelt");
        follow.Clicked += (_, _) => { _follow = !_follow; Refresh(); };
        _refreshers.Add(() => Light(follow, _follow));
        pages.Children.Add(follow);
        bar.Children.Add(pages);

        var steps = Tight();
        var last = Key("LAST");
        ToolTipProperties.SetText(last, "Tik daarna op een stapknop: de laatste stap");
        last.Clicked += (_, _) => { _panel.LastMode = !_panel.LastMode; Refresh(); };
        _refreshers.Add(() =>
        {
            Light(last, _panel.LastMode, Theme.Orange);
            last.Text = _panel.LastMode ? "LAST …" : $"LAST {_seq.Length}";
        });
        var all = Key("ALL");
        ToolTipProperties.SetText(all, "Een encoder verschuift de waarde van alle actieve stappen");
        all.Clicked += (_, _) => { _panel.AllSteps = !_panel.AllSteps; Refresh(); };
        _refreshers.Add(() => Light(all, _panel.AllSteps));
        steps.Children.Add(last);
        steps.Children.Add(all);
        bar.Children.Add(steps);

        if (_drum is { } drum)
        {
            var mute = Key("MUTE");
            ToolTipProperties.SetText(mute, "Een tik op een pad zet het spoor stil of weer aan");
            mute.Clicked += (_, _) => { _muteMode = !_muteMode; Refresh(); };
            _refreshers.Add(() => Light(mute, _muteMode));

            var record = Key("REC");
            ToolTipProperties.SetText(record, "Tijdens het spelen zet een tik op een pad de stap die speelt");
            record.Clicked += (_, _) => { drum.Record = !drum.Record; Refresh(); };
            _refreshers.Add(() =>
            {
                record.BackgroundColor = drum.Record ? Theme.Red : Unlit;
                record.TextColor = Theme.Silk;
            });

            var pads = Tight();
            pads.Children.Add(mute);
            pads.Children.Add(record);
            bar.Children.Add(pads);
        }

        var gear = IconButton("⚙", "Instellingen: mode, rate, swing, randomness, probability …");
        gear.Clicked += (_, _) => Popup.Show(this, Title, _setup, Changed);
        bar.Children.Add(gear);

        // ---- under ⚙: the pad's note, copy, mode, time division, scale, transpose, swing, randomness, probability
        if (_drum is not null)
        {
            var noteDown = IconButton("▼", "Lagere noot voor deze pad");
            noteDown.Clicked += (_, _) => ShiftNote(-1);
            var noteUp = IconButton("▲", "Hogere noot voor deze pad");
            noteUp.Clicked += (_, _) => ShiftNote(1);
            Setting("PAD", Group(noteDown, _noteLabel, noteUp));
        }

        var copy = Key("COPY ▶");
        ToolTipProperties.SetText(copy, "Deze pagina naar de volgende kopiëren (en de reeks zo lang maken)");
        copy.Clicked += (_, _) => CopyPage();
        _refreshers.Add(() =>
        {
            copy.IsEnabled = _panel.Page < BeatStepSequence.PageCount - 1;
            copy.Text = copy.IsEnabled ? $"P{_panel.Page + 1} → P{_panel.Page + 2}" : "COPY ▶";
        });
        Setting("COPY", copy);

        var modes = Tight();
        for (int i = 0; i < BeatStepSequence.DirectionNames.Length; i++)
        {
            var mode = (BeatStepDirection)i;
            var button = Key(BeatStepSequence.DirectionNames[i]);
            button.MinimumWidthRequest = 52;
            button.Clicked += (_, _) => { _seq.Direction = mode; Changed(); };
            _refreshers.Add(() => Light(button, _seq.Direction == mode));
            modes.Children.Add(button);
        }
        Setting("MODE", modes);
        Setting("RATE", Choice(BeatStepSequence.Rates.Select(r => r.Name).ToArray(), _seq.Rate, i => _seq.Rate = i));

        if (_seq is BeatStepSeq melodic)
        {
            Setting("SCALE", Choice(BeatStepSeq.Scales.Select(s => s.Name).ToArray(), melodic.Scale, i => melodic.Scale = i));
            var down = IconButton("▼", "Een halve toon lager");
            down.Clicked += (_, _) => { melodic.Transpose = Math.Max(-24, melodic.Transpose - 1); Changed(); };
            var up = IconButton("▲", "Een halve toon hoger");
            up.Clicked += (_, _) => { melodic.Transpose = Math.Min(24, melodic.Transpose + 1); Changed(); };
            Setting("TRANSPOSE", Group(down, _transposeLabel, up));
        }

        // A tap opens a popup with a slider, on top of the settings
        var swing = ValueButton.Percent("SWING", 0.5, 0.75, () => _seq.Swing, v => _seq.Swing = v);
        var randomness = ValueButton.Percent("RANDOMNESS", 0, 1, () => _seq.Randomness, v => _seq.Randomness = v);
        var probability = ValueButton.Percent("PROBABILITY", 0, 1, () => _seq.Probability, v => _seq.Probability = v);
        ToolTipProperties.SetText(randomness, "Hoe groot de veranderingen zijn");
        ToolTipProperties.SetText(probability, "Hoe vaak er iets verandert");
        foreach (var button in new[] { swing, randomness, probability })
        {
            button.BackgroundColor = Unlit;
            button.Done += (_, _) => _seq.Save();
            Setting("", button);
        }

        var controls = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = bar,
        };

        var grid = new Grid
        {
            Padding = 8,
            RowSpacing = 6,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(controls, 0, 0);
        grid.Add(_panel, 0, 1);
        grid.Add(_panel.Lights, 0, 1);
        Content = grid;

        Refresh();
    }

    /// <summary>Select the pad (and hear it, record it); in mute mode switch its track off or on.</summary>
    void PressPad(int pad)
    {
        if (_drum is null) return;
        if (_muteMode)
            _drum.Tracks[pad].Mute = !_drum.Tracks[pad].Mute;
        else
        {
            _drum.Selected = pad;
            _drum.Hit(pad);
        }
        Changed();
    }

    void ShiftNote(int delta)
    {
        if (_drum is null) return;
        var track = _drum.Tracks[_drum.Selected];
        track.Note = Math.Clamp(track.Note + delta, 0, 127);
        _drum.Hit(_drum.Selected);
        Changed();
    }

    void CopyPage()
    {
        _seq.CopyPage(_panel.Page);
        _panel.Page = Math.Min(_panel.Page + 1, BeatStepSequence.PageCount - 1);
        _follow = false;
        Changed();
    }

    /// <summary>A row under ⚙: the caption, then the control.</summary>
    void Setting(string caption, View view)
    {
        int row = _setup.RowDefinitions.Count;
        _setup.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _setup.Add(Caption(caption), 0, row);
        view.HorizontalOptions = LayoutOptions.Start;
        view.VerticalOptions = LayoutOptions.Center;
        _setup.Add(view, 1, row);
    }

    void Changed()
    {
        _seq.Save();
        Refresh();
    }

    void Refresh()
    {
        _startStop.Text = _seq.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _seq.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _seq.IsRunning ? Theme.Red : Theme.Key;
        if (_seq is BeatStepSeq melodic)
            _transposeLabel.Text = melodic.Transpose > 0 ? $"+{melodic.Transpose}" : $"{melodic.Transpose}";
        if (_drum is not null)
        {
            int note = _drum.Tracks[_drum.Selected].Note;
            _noteLabel.Text = $"{_drum.Selected + 1}: {BeatStepDrum.PadName(note)} ({BeatStepSeq.NoteName(note)})";
        }
        foreach (var refresh in _refreshers)
            refresh();
        _panel.Refresh();
    }

    static void Light(Button button, bool on) => Light(button, on, Lit);

    static void Light(Button button, bool on, Color color)
    {
        button.BackgroundColor = on ? color : Unlit;
        button.TextColor = on ? Colors.Black : Theme.Silk;
    }

    /// <summary>A list to choose from, like SHIFT + pads on the BeatStep Pro.</summary>
    Picker Choice(string[] options, int selected, Action<int> set)
    {
        var picker = new Picker
        {
            ItemsSource = options,
            SelectedIndex = selected,
            MinimumWidthRequest = 140,
            MinimumHeightRequest = 44,
            BackgroundColor = Unlit,
            TextColor = Theme.Silk,
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0) return;
            set(picker.SelectedIndex);
            Changed();
        };
        return picker;
    }

    static Button Key(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        MinimumHeightRequest = 44,
        MinimumWidthRequest = 56,
        Padding = new Thickness(8, 0),
        BackgroundColor = Unlit,
        TextColor = Theme.Silk,
    };

    /// <summary>Buttons that belong together, close side by side.</summary>
    static HorizontalStackLayout Tight() => new() { Spacing = 2, VerticalOptions = LayoutOptions.Center };

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
