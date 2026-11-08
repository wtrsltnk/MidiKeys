namespace MidiKeys.Controls;

/// <summary>Navigation-bar content: the page title on the left, the page's main controls on the right.</summary>
public static class PageHeader
{
    public static void Set(Page page, params View[] controls)
    {
        var bar = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(8, 0),
        };
        var title = new Label
        {
            FontSize = 18,
            TextColor = Theme.Silk,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        title.SetBinding(Label.TextProperty, new Binding(nameof(Page.Title), source: page)); // the title can change later
        bar.Add(title, 0, 0);

        var right = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        foreach (var control in controls)
        {
            control.VerticalOptions = LayoutOptions.Center;
            right.Children.Add(control);
        }
        bar.Add(right, 1, 0);

        Shell.SetTitleView(page, bar);

#if WINDOWS
        // Windows sizes the title view to its content: stretch it to the right edge of the window.
        bar.Loaded += (_, _) => FillToRightEdge(bar);
        page.SizeChanged += (_, _) => FillToRightEdge(bar);
#endif
    }

#if WINDOWS
    static void FillToRightEdge(Grid bar)
    {
        if (bar.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native || native.XamlRoot is null)
            return;
        double left = native.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        double width = native.XamlRoot.Size.Width - left;
        if (width > 0 && Math.Abs(bar.WidthRequest - width) > 0.5)
            bar.WidthRequest = width;
    }
#endif
}
