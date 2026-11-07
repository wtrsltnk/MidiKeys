namespace MidiKeys.Pages;

/// <summary>Placeholder: the arpeggiator comes later.</summary>
public class ArpPage : ContentPage
{
    public ArpPage()
    {
        Title = "Arp";
        BackgroundColor = Color.FromArgb("#1E1E2E");
        Content = new Label
        {
            Text = "Arpeggiator — komt later",
            FontSize = 18,
            TextColor = Color.FromArgb("#A6ADC8"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
    }
}
