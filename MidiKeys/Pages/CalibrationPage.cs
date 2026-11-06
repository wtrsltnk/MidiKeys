using MidiKeys.Controls;
using MidiKeys.Services;

namespace MidiKeys.Pages;

/// <summary>
/// Asks for 6 soft and 6 hard taps, measures contact area and accelerometer
/// peak, and determines per source whether soft and hard are distinguishable.
/// </summary>
public class CalibrationPage : ContentPage
{
    const int TapsPerPhase = 6;

    enum Phase { Soft, Hard, Done }

    readonly VelocitySettings _settings;
    readonly List<NoteEventArgs> _soft = [];
    readonly List<NoteEventArgs> _hard = [];
    readonly Label _instruction = new() { FontSize = 20, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center };
    readonly Label _progress = new() { FontSize = 14, TextColor = Color.FromArgb("#A6ADC8"), HorizontalTextAlignment = TextAlignment.Center };
    readonly Label _lastTap = new() { FontSize = 12, TextColor = Color.FromArgb("#7F849C"), HorizontalTextAlignment = TextAlignment.Center };
    readonly Button _save = new() { Text = "Opslaan", IsEnabled = false, BackgroundColor = Color.FromArgb("#89B4FA"), TextColor = Color.FromArgb("#1E1E2E") };
    readonly PianoKeyboard _keys;

    Phase _phase = Phase.Soft;
    VelocityCalibration? _result;

    public CalibrationPage(VelocitySettings settings)
    {
        _settings = settings;
        BackgroundColor = Color.FromArgb("#1E1E2E");

        _keys = new PianoKeyboard
        {
            WhiteKeyCount = 3,
            BaseNote = 60,
            VelocityConfig = settings.Config with { Mode = VelocityMode.Calibrate, WindowMs = Math.Max(settings.Config.WindowMs, 20) },
        };
        _keys.NoteOn += OnTap;

        var retry = new Button { Text = "Opnieuw", BackgroundColor = Color.FromArgb("#45475A"), TextColor = Colors.White };
        retry.Clicked += (_, _) => Restart();

        var cancel = new Button { Text = "Annuleren", BackgroundColor = Color.FromArgb("#45475A"), TextColor = Colors.White };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();

        _save.Clicked += async (_, _) =>
        {
            if (_result is not null)
                _settings.Update(_settings.Config with { Calibration = _result });
            await Navigation.PopModalAsync();
        };

        var header = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                _instruction,
                _progress,
                _lastTap,
                new Label
                {
                    Text = "Houd of leg het toestel zoals je straks speelt. Tik met één vinger, steeds los.",
                    FontSize = 12, TextColor = Color.FromArgb("#7F849C"), HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };

        var buttons = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, Children = { cancel, retry, _save } };

        var grid = new Grid
        {
            Padding = 12,
            RowSpacing = 10,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        grid.Add(header, 0, 0);
        grid.Add(_keys, 0, 1);
        grid.Add(buttons, 0, 2);
        Content = grid;

        UpdateUi();
    }

    void OnTap(object? sender, NoteEventArgs e)
    {
        _lastTap.Text = $"vlak {e.RawSize:0.0} dp · schok {e.RawAccel:0.00} m/s²";

        switch (_phase)
        {
            case Phase.Soft:
                _soft.Add(e);
                if (_soft.Count >= TapsPerPhase) _phase = Phase.Hard;
                break;
            case Phase.Hard:
                _hard.Add(e);
                if (_hard.Count >= TapsPerPhase) Finish();
                break;
        }
        UpdateUi();
    }

    void Finish()
    {
        _phase = Phase.Done;
        var size = CalibrationMath.Evaluate(_soft.Select(t => t.RawSize), _hard.Select(t => t.RawSize));
        var accel = CalibrationMath.Evaluate(_soft.Select(t => t.RawAccel), _hard.Select(t => t.RawAccel));
        _result = new VelocityCalibration(size.Soft, size.Hard, size.Usable, accel.Soft, accel.Hard, accel.Usable, IsCalibrated: true);
        _save.IsEnabled = true;
    }

    void Restart()
    {
        _soft.Clear();
        _hard.Clear();
        _result = null;
        _phase = Phase.Soft;
        _save.IsEnabled = false;
        _lastTap.Text = "";
        UpdateUi();
    }

    void UpdateUi()
    {
        switch (_phase)
        {
            case Phase.Soft:
                _instruction.Text = "Tik ZACHT op de toetsen";
                _progress.Text = $"{_soft.Count} / {TapsPerPhase}";
                break;
            case Phase.Hard:
                _instruction.Text = "Tik nu HARD op de toetsen";
                _progress.Text = $"{_hard.Count} / {TapsPerPhase}";
                break;
            case Phase.Done:
                _instruction.Text = "Klaar";
                _progress.Text = SettingsPage.DescribeCalibration(_result!);
                break;
        }
    }
}
