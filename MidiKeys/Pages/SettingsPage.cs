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
    readonly Picker _mode = new() { ItemsSource = ModeNames, TextColor = Colors.White };
    readonly Slider _fixed = Make(1, 127);
    readonly Slider _min = Make(1, 127);
    readonly Slider _max = Make(1, 127);
    readonly Slider _window = Make(5, 50);
    readonly Label _calStatus = Text("", 13, "#A6ADC8");

    public SettingsPage(VelocitySettings settings)
    {
        _settings = settings;
        Title = "Aanslag";
        BackgroundColor = Color.FromArgb("#1E1E2E");

        var calibrate = new Button { Text = "Kalibreren…", BackgroundColor = Color.FromArgb("#45475A"), TextColor = Colors.White };
        calibrate.Clicked += async (_, _) =>
        {
            Apply();
            await Navigation.PushModalAsync(new CalibrationPage(_settings));
        };

        var close = new Button { Text = "Klaar", BackgroundColor = Color.FromArgb("#89B4FA"), TextColor = Color.FromArgb("#1E1E2E") };
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
                    Text("Aanslaggevoeligheid", 20, "#FFFFFF"),
                    Text("Bron", 14, "#A6ADC8"), _mode,
                    Row("Vaste velocity", _fixed, v => $"{v:0}"),
                    Row("Minimale velocity (zachtste aanslag)", _min, v => $"{v:0}"),
                    Row("Maximale velocity (hardste aanslag)", _max, v => $"{v:0}"),
                    Row("Meetvenster (extra latency)", _window, v => $"{v:0} ms"),
                    Text("Contactvlak en versnellingsmeter worden een korte tijd na het aanraken gemeten; "
                         + "zo lang wordt de noot uitgesteld. 15–25 ms is meestal een goede balans.", 12, "#7F849C"),
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#45475A"), Margin = new Thickness(0, 6) },
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

    static Label Text(string text, double size, string color) =>
        new() { Text = text, FontSize = size, TextColor = Color.FromArgb(color) };

    static View Row(string title, Slider slider, Func<double, string> format)
    {
        var value = Text(format(slider.Value), 14, "#FFFFFF");
        slider.ValueChanged += (_, e) => value.Text = format(e.NewValue);
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Children = { Text(title, 14, "#A6ADC8"), value },
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
