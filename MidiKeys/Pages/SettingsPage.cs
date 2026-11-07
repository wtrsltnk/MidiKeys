using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

public class SettingsPage : ContentPage
{
    static readonly string[] ModeNames =
    [
        "Vast (schuif)",
        "Positie op toets (lager = harder)",
        "Contactvlak",
        "Versnellingsmeter",
        "Automatisch (gekalibreerde bronnen)",
    ];

    readonly VelocitySettings _settings;
    readonly Picker _mode = new() { ItemsSource = ModeNames, TextColor = Theme.Silk };
    readonly Slider _fixed = Make(1, 127);
    readonly Slider _min = Make(1, 127);
    readonly Slider _max = Make(1, 127);
    readonly Slider _window = Make(5, 50);
    readonly Label _calStatus = Text("", 13, Theme.SilkMid);

    public SettingsPage(VelocitySettings settings)
    {
        _settings = settings;
        Title = "Aanslag";
        BackgroundColor = Theme.Panel;

        var calibrate = new Button { Text = "Kalibreren…", BackgroundColor = Theme.Line, TextColor = Theme.Silk };
        calibrate.Clicked += async (_, _) =>
        {
            Apply();
            await Navigation.PushModalAsync(new CalibrationPage(_settings));
        };

        var close = new Button { Text = "Klaar", BackgroundColor = Theme.Red, TextColor = Theme.Silk };
        close.Clicked += async (_, _) =>
        {
            Apply();
            await Navigation.PopModalAsync();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 10,
                Children =
                {
                    Text("Aanslaggevoeligheid", 20, Theme.Silk),
                    Text("Bron", 14, Theme.SilkMid), _mode,
                    Row("Vaste velocity", _fixed, v => $"{v:0}"),
                    Row("Minimale velocity (zachtste aanslag)", _min, v => $"{v:0}"),
                    Row("Maximale velocity (hardste aanslag)", _max, v => $"{v:0}"),
                    Row("Meetvenster (extra latency)", _window, v => $"{v:0} ms"),
                    Text("Contactvlak en versnellingsmeter worden een korte tijd na het aanraken gemeten; "
                         + "zo lang wordt de noot uitgesteld. 15–25 ms is meestal een goede balans.", 12, Theme.SilkDim),
                    new BoxView { HeightRequest = 1, Color = Theme.Line, Margin = new Thickness(0, 6) },
                    _calStatus,
                    calibrate,
                    close,
                },
            },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var c = _settings.Config;
        _mode.SelectedIndex = c.Mode == VelocityMode.Calibrate ? (int)VelocityMode.Auto : (int)c.Mode;
        _fixed.Value = c.FixedVelocity;
        _min.Value = c.MinVelocity;
        _max.Value = c.MaxVelocity;
        _window.Value = c.WindowMs;
        _calStatus.Text = DescribeCalibration(c.Calibration);
    }

    void Apply()
    {
        var mode = _mode.SelectedIndex >= 0 ? (VelocityMode)_mode.SelectedIndex : VelocityMode.Auto;
        _settings.Update(_settings.Config with
        {
            Mode = mode,
            FixedVelocity = (int)_fixed.Value,
            MinVelocity = (int)_min.Value,
            MaxVelocity = (int)_max.Value,
            WindowMs = (int)_window.Value,
        });
    }

    internal static string DescribeCalibration(VelocityCalibration cal)
    {
        if (!cal.IsCalibrated)
            return "Nog niet gekalibreerd. 'Automatisch' gebruikt nu de positie op de toets.";

        string size = cal.SizeUsable ? $"bruikbaar ({cal.SoftSize:0.0} → {cal.HardSize:0.0} dp)" : "niet bruikbaar";
        string accel = cal.AccelUsable ? $"bruikbaar ({cal.SoftAccel:0.00} → {cal.HardAccel:0.00} m/s²)" : "niet bruikbaar";
        string auto = cal.SizeUsable || cal.AccelUsable ? "" : "\n'Automatisch' valt terug op de positie op de toets.";
        return $"Contactvlak: {size}\nVersnellingsmeter: {accel}{auto}";
    }

    static Slider Make(double min, double max) => new() { Maximum = max, Minimum = min };

    static Label Text(string text, double size, Color color) =>
        new() { Text = text, FontSize = size, TextColor = color };

    static View Row(string title, Slider slider, Func<double, string> format)
    {
        var value = Text(format(slider.Value), 14, Theme.Silk);
        slider.ValueChanged += (_, e) => value.Text = format(e.NewValue);
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Children = { Text(title, 14, Theme.SilkMid), value },
                },
                slider,
            },
        }.Also(g => Grid.SetColumn(value, 1));
    }
}

static class ViewExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
