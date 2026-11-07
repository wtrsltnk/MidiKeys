using MidiKeys.Controls;
using MidiKeys.Pages;
using MidiKeys.Services;

namespace MidiKeys;

/// <summary>
/// Hamburger menu (top left): Piano, then a Sequencer and an Arp section listing what the user
/// added, each on its own MIDI channel, with a + next to each section title to add one on a free channel.
/// </summary>
public class AppShell : Shell
{
    static readonly Color MenuBackground = Theme.PanelDark;
    static readonly Color Selected = Theme.Key;

    readonly Instruments _instruments;
    readonly MusicClock _clock;
    readonly IMidiOutput _midi;
    readonly VelocitySettings _velocity;
    readonly ShellItem _piano;
    readonly Dictionary<StepSequencer, (ShellItem Item, SequencerPage Page)> _sequencers = new();
    readonly Dictionary<Arpeggiator, (ShellItem Item, ArpPage Page)> _arps = new();
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
        foreach (var sequencer in instruments.Sequencers)
            AddPage(sequencer);
        foreach (var arp in instruments.Arps)
            AddPage(arp);

        Navigated += (_, _) => BuildMenu();
        BuildMenu();
    }

    // ---------------------------------------------------------------- pages

    ShellItem AddPage(StepSequencer sequencer)
    {
        var page = new SequencerPage(sequencer);
        ShellItem item = new ShellContent { Title = page.Title, Content = page };
        Items.Add(item);
        _sequencers[sequencer] = (item, page);
        return item;
    }

    ShellItem AddPage(Arpeggiator arp)
    {
        var page = new ArpPage(arp, _velocity);
        ShellItem item = new ShellContent { Title = page.Title, Content = page };
        Items.Add(item);
        _arps[arp] = (item, page);
        return item;
    }

    void Remove(StepSequencer sequencer)
    {
        // Gone from the instruments first: leaving the page rebuilds the menu from that list.
        if (!_sequencers.Remove(sequencer, out var entry)) return;
        _instruments.Remove(sequencer);
        RemoveItem(entry.Item);
        BuildMenu();
    }

    void Remove(Arpeggiator arp)
    {
        // Gone from the instruments first: leaving the page rebuilds the menu from that list.
        if (!_arps.Remove(arp, out var entry)) return;
        _instruments.Remove(arp);
        RemoveItem(entry.Item);
        entry.Page.Close();
        BuildMenu();
    }

    void RemoveItem(ShellItem item)
    {
        if (CurrentItem == item)
            CurrentItem = _piano;
        Items.Remove(item);
    }

    void Go(ShellItem item)
    {
        CurrentItem = item;
        FlyoutIsPresented = false;
    }

    async Task<int?> PickChannel()
    {
        var free = _instruments.FreeChannels;
        if (free.Count == 0)
        {
            await DisplayAlertAsync("Geen kanaal vrij", "Alle 16 MIDI-kanalen zijn al in gebruik.", "OK");
            return null;
        }

        var options = free.Select(ch => $"Kanaal {ch + 1}").ToArray();
        var choice = await DisplayActionSheetAsync("MIDI-kanaal", "Annuleren", null, options);
        int index = Array.IndexOf(options, choice);
        return index >= 0 ? free[index] : null;
    }

    async Task AddSequencer()
    {
        if (await PickChannel() is int channel)
            Go(AddPage(_instruments.AddSequencer(channel)));
        BuildMenu();
    }

    async Task AddArp()
    {
        if (await PickChannel() is int channel)
            Go(AddPage(_instruments.AddArp(channel)));
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

        _menu.Children.Add(Header("Sequencer", AddSequencer));
        foreach (var sequencer in _instruments.Sequencers)
            _menu.Children.Add(MenuEntry(_sequencers[sequencer].Item.Title, _sequencers[sequencer].Item, () => Remove(sequencer)));

        _menu.Children.Add(Header("Arp", AddArp));
        foreach (var arp in _instruments.Arps)
            _menu.Children.Add(MenuEntry(_arps[arp].Item.Title, _arps[arp].Item, () => Remove(arp)));
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
                if (await DisplayAlertAsync("Verwijderen", $"{text} en de instellingen verwijderen?", "Verwijderen", "Annuleren"))
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
