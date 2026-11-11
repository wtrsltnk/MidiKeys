namespace MidiKeys.Controls;

/// <summary>A card over the page with a dimmed backdrop; OK or a tap beside it closes it.</summary>
public static class Popup
{
    /// <summary>Shows <paramref name="content"/> under <paramref name="caption"/>; it scrolls when it is taller than the page.</summary>
    public static void Show(ContentPage page, string caption, View content, Action? closed = null)
    {
        var host = Host(page);

        var scroll = new ScrollView { Content = content };
        View? overlay = null;
        void Close()
        {
            if (overlay is null) return;
            scroll.Content = null; // free the content, so a next popup can show it again
            host.Children.Remove(overlay);
            overlay = null;
            closed?.Invoke();
        }

        var ok = new Button { Text = "OK", FontAttributes = FontAttributes.Bold, HeightRequest = 48, BackgroundColor = Theme.Orange, TextColor = Theme.Ink };
        ok.Clicked += (_, _) => Close();

        var layout = new Grid
        {
            RowSpacing = 14,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        layout.Add(Theme.Print(caption), 0, 0);
        layout.Add(scroll, 0, 1);
        layout.Add(ok, 0, 2);

        var card = new Border
        {
            BackgroundColor = Theme.PanelDark,
            Stroke = Theme.Line,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = 20,
            WidthRequest = Math.Min(560, Math.Max(300, page.Width - 32)),
            MaximumHeightRequest = Math.Max(200, page.Height - 32),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = layout,
        };

        var backdrop = new BoxView { Color = Colors.Black.WithAlpha(0.55f) };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Close();
        backdrop.GestureRecognizers.Add(tap);

        var layer = new Grid { Children = { backdrop, card } };
        overlay = layer;
        host.Children.Add(layer);
    }

    /// <summary>The page content wrapped once in a grid a popup can lie on top of.</summary>
    public static Grid Host(ContentPage page)
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
