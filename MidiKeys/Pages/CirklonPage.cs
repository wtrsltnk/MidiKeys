using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// One track of a Sequentix Cirklon2 with a P3 pattern: choose the row, then drag the bars on the
/// screen to change the values and tap the step keys to switch the flags. Keeps running when you
/// switch to another page.
/// </summary>
public class CirklonPage : ContentPage
{
    static readonly Color KeyWhite = Color.FromArgb("#ECECE6");
    static readonly Color KeyDark = Color.FromArgb("#2A2A2A");
    static readonly string[] LogoColors = ["#FF4D4D", "#FF9A3D", "#FFE14D", "#6BE36B", "#4DD8FF", "#4D8BFF", "#A66BFF"];

    readonly CirklonSequencer _ck;
    readonly CirklonPanel _panel;
    readonly List<Action> _refreshers = new();
    readonly List<Button> _rowButtons = new();
    readonly Grid _rows = new() { ColumnSpacing = 4, RowSpacing = 4 };

    readonly Button _startStop = IconButton("▶", "Start");
    readonly Label _barLabel = Text("");
    readonly Label _barsLabel = Text("");
    readonly Label _ccLabel = Text("");
    readonly View _ccGroup;
    bool _follow;

    public CirklonPage(CirklonSequencer ck)
    {
        _ck = ck;
        Title = $"CIRKLON2 (ch{ck.Channel + 1})";
        BackgroundColor = Color.FromArgb("#0E0E0E");

        _panel = new CirklonPanel(ck);
        _panel.Changed += (_, _) => _ck.Save();
        _panel.KeysChanged += (_, _) => Refresh();

        // Positions arrive on the clock thread; hop to the UI thread for drawing.
        _ck.StepChanged += (bar, step) => Dispatcher.Dispatch(() =>
        {
            if (_follow && bar >= 0 && bar != _panel.EditBar)
            {
                _panel.EditBar = bar;
                Refresh();
            }
            _panel.ShowPosition(bar, step);
        });
        _ck.RunningChanged += (_, _) => Refresh();

        // ---- title bar: start/stop and back to step 1 (the tempo is in the side menu)
        _startStop.Clicked += (_, _) => { if (_ck.IsRunning) _ck.Stop(); else _ck.Start(); };
        var reset = IconButton("⏮", "Terug naar stap 1");
        reset.Clicked += (_, _) => _ck.Reset();
        PageHeader.Set(this, _startStop, reset);

        // ---- ROW: which row the screen shows and the drags edit
        for (int i = 0; i < CirklonPanel.RowNames.Length; i++)
        {
            var row = (CirklonRow)i;
            var color = CirklonPanel.RowColors[i];
            var button = Key(CirklonPanel.RowNames[i].ToUpperInvariant());
            button.Clicked += (_, _) => { _panel.Row = row; Refresh(); };
            _refreshers.Add(() =>
            {
                bool on = _panel.Row == row;
                button.BackgroundColor = on ? color : KeyDark;
                button.TextColor = on ? Colors.Black : Theme.Silk;
            });
            _rowButtons.Add(button);
        }
        ArrangeRows(8);
        SizeChanged += (_, _) => ArrangeRows(Width >= 1000 || Width <= 0 ? 8 : 4);

        var logo = new Label { FontSize = 34, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
        var spans = new FormattedString();
        for (int i = 0; i < "cirklon".Length; i++)
            spans.Spans.Add(new Span { Text = "cirklon"[i].ToString(), TextColor = Color.FromArgb(LogoColors[i]) });
        logo.FormattedText = spans;

        var header = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
        };
        header.Add(logo, 0, 0);
        header.Add(Caption("ROW"), 1, 0);
        header.Add(_rows, 2, 0);

        // ---- the step keys, direction, timebase, bars and the aux CC
        var keys = new HorizontalStackLayout { Spacing = 2 };
        foreach (var (mode, label, tip) in new[]
                 {
                     (CirklonKeys.Gate, "GATE", "Toetsen zetten de gate (op een aux-rij: de aux aan/uit)"),
                     (CirklonKeys.Tie, "TIE", "Toetsen zetten tie: de noot klinkt door tot de volgende stap"),
                     (CirklonKeys.Skip, "SKIP", "Toetsen zetten skip: de stap wordt overgeslagen"),
                     (CirklonKeys.Last, "LAST", "Tik op een staptoets: de laatste stap van deze maat"),
                 })
        {
            var button = Key(label);
            ToolTipProperties.SetText(button, tip);
            button.Clicked += (_, _) => { _panel.Keys = mode; Refresh(); };
            _refreshers.Add(() =>
            {
                bool on = _panel.Keys == mode;
                button.Text = mode == CirklonKeys.Gate && _panel.Row >= CirklonRow.AuxA ? CirklonPanel.RowNames[(int)_panel.Row].ToUpperInvariant() : label;
                button.BackgroundColor = on ? KeyWhite : KeyDark;
                button.TextColor = on ? Colors.Black : Theme.Silk;
            });
            keys.Children.Add(button);
        }

        var direction = Choice(CirklonSequencer.DirectionNames, (int)_ck.Direction, i => _ck.Direction = (CirklonDirection)i);
        var timebase = Choice(CirklonSequencer.Timebases.Select(t => t.Name).ToArray(), _ck.Timebase, i => _ck.Timebase = i);

        var barDown = IconButton("◀", "Vorige maat");
        barDown.Clicked += (_, _) => ShowBar(_panel.EditBar - 1);
        var barUp = IconButton("▶", "Volgende maat");
        barUp.Clicked += (_, _) => ShowBar(_panel.EditBar + 1);
        var follow = Key("FOLLOW");
        ToolTipProperties.SetText(follow, "Bar follow: de weergave volgt de maat die speelt");
        follow.Clicked += (_, _) => { _follow = !_follow; Refresh(); };
        _refreshers.Add(() =>
        {
            follow.BackgroundColor = _follow ? KeyWhite : KeyDark;
            follow.TextColor = _follow ? Colors.Black : Theme.Silk;
        });

        var fewer = IconButton("−", "Maat eraf");
        fewer.Clicked += (_, _) => SetBars(_ck.Bars.Count - 1);
        var more = IconButton("+", "Maat erbij (een kopie van de laatste)");
        more.Clicked += (_, _) => SetBars(_ck.Bars.Count + 1);

        var ccDown = IconButton("▼", "Lager CC-nummer");
        ccDown.Clicked += (_, _) => ShiftCc(-1);
        var ccUp = IconButton("▲", "Hoger CC-nummer");
        ccUp.Clicked += (_, _) => ShiftCc(1);
        _ccGroup = Group(Caption("CC"), ccDown, _ccLabel, ccUp);

        var settings = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (var view in new View[]
                 {
                     Group(Caption("KEYS"), keys),
                     Group(Caption("DIRECTION"), direction),
                     Group(Caption("TIMEBASE"), timebase),
                     Group(Caption("BAR"), barDown, _barLabel, barUp, follow),
                     Group(Caption("BARS"), fewer, _barsLabel, more),
                     _ccGroup,
                 })
        {
            view.Margin = new Thickness(0, 0, 24, 6);
            settings.Children.Add(view);
        }

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

    void ShowBar(int bar)
    {
        _panel.EditBar = Math.Clamp(bar, 0, _ck.Bars.Count - 1);
        _follow = false; // as on the Cirklon: choosing a bar yourself ends bar follow
        Refresh();
    }

    void SetBars(int count)
    {
        _ck.SetBarCount(count);
        _ck.Save();
        _panel.EditBar = Math.Min(_panel.EditBar, _ck.Bars.Count - 1);
        Refresh();
    }

    void ShiftCc(int delta)
    {
        int aux = (int)_panel.Row - (int)CirklonRow.AuxA;
        if (aux < 0) return;
        _ck.AuxCc[aux] = Math.Clamp(_ck.AuxCc[aux] + delta, 0, 127);
        _ck.Save();
        Refresh();
    }

    void Refresh()
    {
        _startStop.Text = _ck.IsRunning ? "■" : "▶";
        ToolTipProperties.SetText(_startStop, _ck.IsRunning ? "Stop" : "Start");
        _startStop.BackgroundColor = _ck.IsRunning ? Theme.Red : Theme.Key;
        _barLabel.Text = $"{_panel.EditBar + 1}/{_ck.Bars.Count}";
        _barsLabel.Text = $"{_ck.Bars.Count}";

        int aux = (int)_panel.Row - (int)CirklonRow.AuxA;
        _ccGroup.IsVisible = aux >= 0;
        if (aux >= 0)
            _ccLabel.Text = $"{_ck.AuxCc[aux]}";

        foreach (var refresh in _refreshers)
            refresh();
        _panel.Refresh();
    }

    /// <summary>The row buttons in equal columns: all eight in one row, or two rows of four.</summary>
    void ArrangeRows(int columns)
    {
        if (_rows.ColumnDefinitions.Count == columns) return;
        _rows.Children.Clear();
        _rows.ColumnDefinitions.Clear();
        _rows.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++)
            _rows.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < _rowButtons.Count / columns; r++)
            _rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < _rowButtons.Count; i++)
            _rows.Add(_rowButtons[i], i % columns, i / columns);
    }

    /// <summary>A list to choose from, like the Cirklon's selection lists.</summary>
    Picker Choice(string[] options, int selected, Action<int> set)
    {
        var picker = new Picker
        {
            ItemsSource = options,
            SelectedIndex = selected,
            MinimumWidthRequest = 120,
            MinimumHeightRequest = 44,
            BackgroundColor = KeyDark,
            TextColor = Theme.Silk,
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0) return;
            set(picker.SelectedIndex);
            _ck.Save();
            Refresh();
        };
        return picker;
    }

    static Button Key(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        MinimumHeightRequest = 44,
        MinimumWidthRequest = 64,
        Padding = new Thickness(6, 0),
        BackgroundColor = KeyDark,
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

    static Label Text(string text) =>
        new() { Text = text, FontSize = 16, TextColor = Theme.Silk, VerticalOptions = LayoutOptions.Center };
}
