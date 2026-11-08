using MidiKeys.Controls;
using MidiKeys.Pages;
using MidiKeys.Services;

namespace MidiKeys;

/// <summary>
/// Hamburger menu (top left): Piano, then a section per MIDI channel listing the views the user
/// added to it (1601 sequencers, BQ-10s, Cirklon2s and arps), with a + to add a view; below them a + for a new channel.
/// </summary>
public class AppShell : Shell
{
    const string SequencerName = "1601 SEQUENCER";
    const string BqName = "BQ-10";
    const string CirklonName = "CIRKLON2";
    const string ArpName = "ARP";

    static readonly Color MenuBackground = Theme.PanelDark;
    static readonly Color Selected = Theme.Key;

    readonly Instruments _instruments;
    readonly MusicClock _clock;
    readonly IMidiOutput _midi;
    readonly VelocitySettings _velocity;
    readonly ShellItem _piano;
    readonly Dictionary<IInstrument, (ShellItem Item, Page Page)> _views = new();
    readonly VerticalStackLayout _menu = new() { Padding = new Thickness(0, 12) };

    public AppShell(MainPage piano, Instruments instruments, MusicClock clock, IMidiOutput midi, VelocitySettings velocity)
    {
        _instruments = instruments;
        _clock = clock;
        _midi = midi;
        _velocity = velocity;

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutBackgroundColor = MenuBackground;
        // Menu on top; the shared tempo and the MIDI device choice right at the bottom.
        var flyout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        flyout.Add(new ScrollView { Content = _menu }, 0, 0);
        flyout.Add(new VerticalStackLayout
        {
            Children = { Line(), TempoControl(), Line(), new MidiDevicePicker(midi) { Padding = new Thickness(20, 12, 12, 12) } },
        }, 0, 1);
        FlyoutContent = flyout;

        SetBackgroundColor(this, MenuBackground);
        SetForegroundColor(this, Theme.Silk);
        SetTitleColor(this, Theme.Silk);

        _piano = new ShellContent { Title = "Piano", Route = "piano", Content = piano };
        Items.Add(_piano);
        foreach (var instrument in instruments.All)
            AddPage(instrument);

        Navigated += (_, _) => BuildMenu();
        BuildMenu();
    }

    // ---------------------------------------------------------------- pages

    ShellItem AddPage(IInstrument instrument)
    {
        Page page = instrument switch
        {
            StepSequencer sequencer => new SequencerPage(sequencer),
            BqSequencer bq => new BqPage(bq),
            CirklonSequencer ck => new CirklonPage(ck),
            Arpeggiator arp => new ArpPage(arp, _velocity),
            _ => throw new ArgumentException("Unknown instrument", nameof(instrument)),
        };
        ShellItem item = new ShellContent { Content = page };
        Items.Add(item);
        _views[instrument] = (item, page);
        UpdateTitles();
        return item;
    }

    void Remove(IInstrument instrument)
    {
        // Gone from the instruments first: leaving the page rebuilds the menu from that list.
        if (!_views.Remove(instrument, out var entry)) return;
        _instruments.Remove(instrument);
        if (CurrentItem == entry.Item)
            CurrentItem = _piano;
        Items.Remove(entry.Item);
        if (entry.Page is ArpPage arpPage)
            arpPage.Close();
        UpdateTitles();
        BuildMenu();
    }

    void Go(ShellItem item)
    {
        CurrentItem = item;
        FlyoutIsPresented = false;
    }

    /// <summary>Name in the menu: the kind, numbered when a channel has more than one of it.</summary>
    string Name(IInstrument instrument)
    {
        string kind = instrument switch { StepSequencer => SequencerName, BqSequencer => BqName, CirklonSequencer => CirklonName, _ => ArpName };
        var same = _instruments.All.Where(i => i.Channel == instrument.Channel && i.GetType() == instrument.GetType()).ToList();
        return same.Count > 1 ? $"{kind} {same.IndexOf(instrument) + 1}" : kind;
    }

    void UpdateTitles()
    {
        foreach (var (instrument, (item, page)) in _views)
            item.Title = page.Title = $"{Name(instrument)} (ch{instrument.Channel + 1})";
    }

    async Task NewChannel()
    {
        var free = _instruments.FreeChannels;
        if (free.Count == 0)
        {
            await DisplayAlertAsync("Geen kanaal vrij", "Alle 16 MIDI-kanalen hebben al een view.", "OK");
            return;
        }

        var options = free.Select(ch => $"Kanaal {ch + 1}").ToArray();
        int index = Array.IndexOf(options, await DisplayActionSheetAsync("MIDI-kanaal", "Annuleren", null, options));
        if (index >= 0)
            await AddView(free[index]);
    }

    async Task AddView(int channel)
    {
        string? choice = await DisplayActionSheetAsync($"View op kanaal {channel + 1}", "Annuleren", null, SequencerName, BqName, CirklonName, ArpName);
        IInstrument? added = choice switch
        {
            SequencerName => _instruments.AddSequencer(channel),
            BqName => _instruments.AddBq(channel),
            CirklonName => _instruments.AddCirklon(channel),
            ArpName => _instruments.AddArp(channel),
            _ => null,
        };
        if (added is not null)
            Go(AddPage(added));
        BuildMenu();
    }

    // ---------------------------------------------------------------- menu

    /// <summary>Tempo for all sequencers and arps.</summary>
    View TempoControl()
    {
        var value = new Label { FontSize = 16, TextColor = Theme.Silk, VerticalOptions = LayoutOptions.Center };
        var slider = new Slider { Minimum = 30, Maximum = 300, Value = _clock.Bpm };
        slider.ValueChanged += (_, e) => _clock.Bpm = (int)Math.Round(e.NewValue);
        slider.DragCompleted += (_, _) => _clock.Save();
        _clock.TempoChanged += (_, _) => Dispatcher.Dispatch(() => { slider.Value = _clock.Bpm; value.Text = $"{_clock.Bpm} BPM"; });
        value.Text = $"{_clock.Bpm} BPM";

        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(new Label
        {
            Text = "TEMPO",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Orange,
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        header.Add(value, 1, 0);

        return new VerticalStackLayout { Padding = new Thickness(20, 12, 12, 8), Spacing = 4, Children = { header, slider } };
    }

    static BoxView Line() => new() { HeightRequest = 1, Color = Theme.Line, Margin = new Thickness(12, 0) };

    void BuildMenu()
    {
        _menu.Children.Clear();
        _menu.Children.Add(MenuEntry("Piano", _piano));

        foreach (int channel in _instruments.UsedChannels)
        {
            _menu.Children.Add(Header($"Kanaal {channel + 1}", () => AddView(channel)));
            foreach (var instrument in _instruments.All.Where(i => i.Channel == channel))
                _menu.Children.Add(MenuEntry(Name(instrument), _views[instrument].Item, () => Remove(instrument)));
        }

        _menu.Children.Add(Header("Nieuw kanaal", NewChannel));
    }

    /// <param name="remove">Shows a trash can that removes the entry (after asking).</param>
    View MenuEntry(string text, ShellItem target, Action? remove = null)
    {
        var label = new Label { Text = text, FontSize = 16, TextColor = Theme.Silk, Padding = new Thickness(20, 12) };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Go(target);
        label.GestureRecognizers.Add(tap); // on the label only, so a tap on the trash can doesn't navigate

        var row = new Grid
        {
            BackgroundColor = CurrentItem == target ? Selected : Colors.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        row.Add(label, 0, 0);

        if (remove is not null)
        {
            var trash = new Button
            {
                Text = "🗑",
                FontSize = 16,
                WidthRequest = 44,
                HeightRequest = 36,
                Padding = 0,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                TextColor = Theme.SilkMid,
            };
            trash.Clicked += async (_, _) =>
            {
                if (await DisplayAlertAsync("Verwijderen", $"{target.Title} en de instellingen verwijderen?", "Verwijderen", "Annuleren"))
                    remove();
            };
            row.Add(trash, 1, 0);
        }
        return row;
    }

    /// <summary>Section title with a line above it and its + button on the right.</summary>
    static View Header(string text, Func<Task> add)
    {
        var header = new Grid
        {
            Margin = new Thickness(20, 8, 12, 4),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Orange,
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);
        header.Add(PlusButton(add), 1, 0);

        var line = Line();
        return new VerticalStackLayout { Margin = new Thickness(0, 12, 0, 0), Children = { line, header } };
    }

    static View PlusButton(Func<Task> add)
    {
        var button = new Button
        {
            Text = "+",
            FontSize = 18,
            WidthRequest = 44,
            HeightRequest = 36,
            Padding = 0,
            BackgroundColor = Selected,
            TextColor = Theme.Silk,
        };
        button.Clicked += async (_, _) => await add();
        return button;
    }
}
