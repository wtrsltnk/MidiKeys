using MidiKeys.Services;

namespace MidiKeys.Controls;

/// <summary>
/// A compact "CAPTION: value" button for the title bar. A tap opens a popup over the page with the
/// value in large type, a slider, − / + and OK; changes apply while you adjust them.
/// </summary>
public class ValueButton : Button
{
    readonly string _caption;
    readonly Func<double> _get;
    readonly Action<double> _set;
    readonly Func<double, string> _format;
    readonly double _min, _max, _step;

    /// <summary>Raised when the popup closes (to save the value).</summary>
    public event EventHandler? Done;

    /// <param name="step">Size of a − / + step, and the slider is rounded to it.</param>
    public ValueButton(string caption, double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> format)
    {
        (_caption, _min, _max, _step, _get, _set, _format) = (caption, min, max, step, get, set, format);
        FontAttributes = FontAttributes.Bold;
        FontSize = 14;
        MinimumHeightRequest = 44;
        Padding = new Thickness(10, 0);
        BackgroundColor = Theme.Key;
        TextColor = Theme.Silk;
        Clicked += (_, _) => Open();
        Refresh();
    }

    /// <summary>The shared tempo; follows changes made elsewhere (side menu, other pages).</summary>
    public static ValueButton Tempo(MusicClock clock)
    {
        var button = new ValueButton("TEMPO", 30, 300, 1, () => clock.Bpm, v => clock.Bpm = (int)Math.Round(v), v => $"{v:0} BPM");
        button.Done += (_, _) => clock.Save();
        EventHandler changed = (_, _) => button.Dispatcher.Dispatch(button.Refresh);
        button.Loaded += (_, _) => { clock.TempoChanged += changed; button.Refresh(); };
        button.Unloaded += (_, _) => clock.TempoChanged -= changed;
        return button;
    }

    /// <summary>A gate length as a fraction of a step, shown in percent.</summary>
    public static ValueButton Percent(string caption, double min, double max, Func<double> get, Action<double> set) =>
        new(caption, min * 100, max * 100, 1, () => get() * 100, v => set(v / 100), v => $"{v:0}%");

    public void Refresh() => Text = $"{_caption}: {_format(_get())}";

    void Open()
    {
        if (Shell.Current?.CurrentPage is not ContentPage page) return;
        var host = Host(page);

        var value = new Label
        {
            FontSize = 40,
            FontAttributes = FontAttributes.Bold,
            TextColor = Theme.Silk,
            HorizontalOptions = LayoutOptions.Center,
        };
        var slider = new Slider { Minimum = _min, Maximum = _max, Value = _get() };
        void Apply(double v)
        {
            v = Math.Clamp(Math.Round(v / _step) * _step, _min, _max);
            _set(v);
            value.Text = _format(_get());
            Refresh();
        }
        slider.ValueChanged += (_, e) => Apply(e.NewValue);

        Button StepButton(string text, double delta)
        {
            var button = new Button { Text = text, FontSize = 22, WidthRequest = 64, HeightRequest = 52, BackgroundColor = Theme.Key, TextColor = Theme.Silk };
            button.Clicked += (_, _) => slider.Value = Math.Clamp(_get() + delta, _min, _max);
            return button;
        }

        View? overlay = null;
        void Close()
        {
            if (overlay is null) return;
            host.Children.Remove(overlay);
            overlay = null;
            Done?.Invoke(this, EventArgs.Empty);
        }

        var ok = new Button { Text = "OK", FontAttributes = FontAttributes.Bold, HeightRequest = 48, BackgroundColor = Theme.Orange, TextColor = Theme.Ink };
        ok.Clicked += (_, _) => Close();

        var row = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        row.Add(StepButton("−", -_step), 0, 0);
        row.Add(slider, 1, 0);
        row.Add(StepButton("+", _step), 2, 0);
        slider.VerticalOptions = LayoutOptions.Center;

        var card = new Border
        {
            BackgroundColor = Theme.PanelDark,
            Stroke = Theme.Line,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = 20,
            WidthRequest = 440,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { Theme.Print(_caption), value, row, ok },
            },
        };

        // A dimmed backdrop; tapping it closes the popup too.
        var backdrop = new BoxView { Color = Colors.Black.WithAlpha(0.55f) };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Close();
        backdrop.GestureRecognizers.Add(tap);

        var layer = new Grid { Children = { backdrop, card } };
        overlay = layer;
        host.Children.Add(layer);
        Apply(_get());
    }

    /// <summary>The page content wrapped once in a grid the popup can lie on top of.</summary>
    static Grid Host(ContentPage page)
    {
        const string id = "value-popup-host";
        if (page.Content is Grid { ClassId: id } host)
            return host;
        var content = page.Content;
        page.Content = null;
        host = new Grid { ClassId = id };
        if (content is not null)
            host.Children.Add(content);
        page.Content = host;
        return host;
    }
}
