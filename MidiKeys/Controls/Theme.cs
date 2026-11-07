namespace MidiKeys.Controls;

/// <summary>
/// TR-808 style for the whole app: charcoal panel, off-white print, orange accents, and step
/// buttons in the red / orange / yellow / cream groups of four.
/// </summary>
public static class Theme
{
    public static readonly Color Panel = Color.FromArgb("#1E1E1E");
    public static readonly Color PanelDark = Color.FromArgb("#141414");    // side menu and title bar
    public static readonly Color Silk = Color.FromArgb("#F2EFE6");         // panel print
    public static readonly Color SilkMid = Color.FromArgb("#BDB8AC");
    public static readonly Color SilkDim = Color.FromArgb("#7D796F");
    public static readonly Color Line = Color.FromArgb("#3C3C3C");
    public static readonly Color Key = Color.FromArgb("#333333");          // button, off
    public static readonly Color Ink = Color.FromArgb("#1E1E1E");          // text on a bright button

    public static readonly Color Red = Color.FromArgb("#D7262B");          // running
    public static readonly Color Orange = Color.FromArgb("#F07D19");       // switch on, accents
    public static readonly Color Yellow = Color.FromArgb("#F2C618");
    public static readonly Color Cream = Color.FromArgb("#EFE9D6");

    public static readonly Color Cap = Cream;                              // fader cap
    public static readonly Color FaderTrack = Color.FromArgb("#444444");
    public static readonly Color LedOn = Color.FromArgb("#FF2A1A");
    public static readonly Color LedOff = Color.FromArgb("#3A1A16");

    static readonly Color[] StepColors = [Red, Orange, Yellow, Cream];

    /// <summary>Step button colour: red, orange, yellow, cream, each for a group of steps.</summary>
    public static Color StepColor(int step, int groupSize = 4) => StepColors[step / groupSize % StepColors.Length];

    /// <summary>Default styles, so controls that set nothing themselves still look like the panel.</summary>
    public static void Register(ResourceDictionary resources)
    {
        resources.Add(new Style(typeof(Label))
        {
            Setters = { new Setter { Property = Label.TextColorProperty, Value = Silk } },
        });
        resources.Add(new Style(typeof(Button))
        {
            Setters =
            {
                new Setter { Property = Button.BackgroundColorProperty, Value = Key },
                new Setter { Property = Button.TextColorProperty, Value = Silk },
                new Setter { Property = Button.CornerRadiusProperty, Value = 2 },
            },
        });
        resources.Add(new Style(typeof(Slider))
        {
            Setters =
            {
                new Setter { Property = Slider.ThumbColorProperty, Value = Cap },
                new Setter { Property = Slider.MinimumTrackColorProperty, Value = Orange },
                new Setter { Property = Slider.MaximumTrackColorProperty, Value = FaderTrack },
            },
        });
        resources.Add(new Style(typeof(Picker))
        {
            Setters =
            {
                new Setter { Property = Picker.TextColorProperty, Value = Silk },
                new Setter { Property = Picker.TitleColorProperty, Value = SilkMid },
            },
        });
    }

    /// <summary>Panel print: a small bold caption such as GATE or MODUS.</summary>
    public static Label Print(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = Silk,
        VerticalOptions = LayoutOptions.Center,
    };

    /// <summary>A switch that is on lights orange.</summary>
    public static void Light(Button button, bool on) => Light(button, on, Orange);

    public static void Light(Button button, bool on, Color color)
    {
        button.BackgroundColor = on ? color : Key;
        button.TextColor = on ? Ink : Silk;
    }

    public static BoxView Divider() => new() { HeightRequest = 1, Color = Line, Margin = new Thickness(12, 0) };
}
