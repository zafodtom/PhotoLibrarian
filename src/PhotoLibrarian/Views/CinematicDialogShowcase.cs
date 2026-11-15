using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;

namespace PhotoLibrarian.Views;

/// <summary>
/// Design-only cinematic dialog specimens. No filesystem, database or album operations.
/// Production dialog handlers are intentionally not connected to this factory.
/// </summary>
internal static class CinematicDialogShowcase
{
    internal enum Variant { Fatal, Warning, Complete }

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(255, r, g, b));

    private static readonly FontFamily TechnicalFont = new("Consolas");

    internal static ContentDialog Create(XamlRoot root, Variant variant)
    {
        var fatal = variant == Variant.Fatal;
        var warning = variant == Variant.Warning;
        var accent = fatal ? Brush(248, 43, 55) : warning ? Brush(255, 191, 40) : Brush(64, 204, 211);
        var dim = fatal ? Brush(47, 11, 15) : warning ? Brush(43, 31, 10) : Brush(9, 37, 41);
        var caption = fatal ? "SYSTEM ALERT // E-1047" : warning ? "USER CONFIRMATION // ALBUM" : "SYSTEM STATUS // SCAN";
        var subject = fatal ? "DATABASE INTEGRITY FAILURE" : warning ? "DELETE SELECTED ALBUM?" : "ALBUM SCAN FINISHED";
        var detail = fatal
            ? "The album database cannot be accessed safely. The current operation was stopped to protect your data."
            : warning
                ? "This is a visual demonstration only. In the real application, deleting an album requires explicit confirmation."
                : "This example represents a successfully completed album scan. No actual files were scanned.";
        var glyph = fatal || warning ? "\uE7BA" : "\uE73E";
        // ContentDialog is capped by the WinUI default width (548 px) unless
        // its template resource is overridden. Keep the size responsive, too.
        var availableWidth = Math.Max(380, root.Size.Width - 64);
        var dialogWidth = Math.Min(800, availableWidth);
        var panelWidth = dialogWidth - 80;
        var compact = dialogWidth < 660;
        var heroFont = compact ? (dialogWidth < 500 ? 39 : 49) : 72;
        var warningFont = compact ? (dialogWidth < 500 ? 35 : 43) : 64;

        var stack = new StackPanel { Spacing = 13 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = Label(caption, 11, accent, true);
        header.Children.Add(headerText);
        var safe = Label("PREVIEW ONLY", 10, Brush(153, 169, 177), false);
        Grid.SetColumn(safe, 1);
        header.Children.Add(safe);
        stack.Children.Add(header);

        // Industrial hazard stripes occupy their own region, not just a colored border.
        // Short angular stripe segments stay inside the available width.
        var hazard = new Grid { Height = 13, ColumnSpacing = 5 };
        for (var i = 0; i < 13; i++)
        {
            hazard.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(i % 4 == 0 ? 1.7 : 1, GridUnitType.Star)
            });
            var segment = new Polygon
            {
                Points = new Microsoft.UI.Xaml.Media.PointCollection
                {
                    new Windows.Foundation.Point(5, 0),
                    new Windows.Foundation.Point(100, 0),
                    new Windows.Foundation.Point(95, 13),
                    new Windows.Foundation.Point(0, 13)
                },
                Stretch = Stretch.Fill,
                Fill = accent,
                Opacity = i % 4 == 0 ? 1 : 0.52
            };
            Grid.SetColumn(segment, i);
            hazard.Children.Add(segment);
        }
        stack.Children.Add(hazard);

        var hero = new Grid { Background = dim, Padding = new Thickness(14, 16, 12, 18) };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 88 : 124) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var emblem = new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(3),
            Width = compact ? 72 : 104,
            Height = compact ? 72 : 104,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = compact ? 49 : 73,
                Foreground = accent
            }
        };
        hero.Children.Add(emblem);
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        if (fatal)
        {
            titleStack.Children.Add(Label("FATAL", heroFont, accent, true));
            titleStack.Children.Add(Label("ERROR", heroFont, accent, true));
        }
        else if (warning)
            titleStack.Children.Add(Label("WARNING", warningFont, accent, true));
        else
        {
            titleStack.Children.Add(Label("PROCESS", compact ? 32 : 52, accent, true));
            titleStack.Children.Add(Label("COMPLETE", compact ? 32 : 52, accent, true));
        }
        Grid.SetColumn(titleStack, 1);
        hero.Children.Add(titleStack);
        stack.Children.Add(hero);
        // A secondary mechanical indicator: the frame changes intensity with
        // severity without adding a fake status or unnecessary motion.
        stack.Children.Add(new Rectangle
        {
            Height = fatal ? 5 : 3,
            Fill = accent,
            Opacity = fatal ? 1 : 0.78
        });

        var subjectBorder = new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(2, 0, 2, 2),
            Background = dim,
            Padding = new Thickness(13, 9, 13, 9),
            Child = Label(subject, 19, Brush(242, 244, 245), true)
        };
        stack.Children.Add(subjectBorder);
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = Brush(215, 224, 229),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = panelWidth - 88
        });

        if (warning)
        {
            stack.Children.Add(new Border
            {
                Background = Brush(25, 29, 31),
                Padding = new Thickness(12),
                Child = Label("TARGET  //  Family Photos (example)", 13, Brush(230, 233, 236), false)
            });
        }
        else if (variant == Variant.Complete)
            stack.Children.Add(Label("PROCESSED  12,846    |    UPDATED  12,846    |    ERRORS  0   (EXAMPLE)", 12, accent, false));
        else
            stack.Children.Add(Label("SYSTEM STATE  //  OPERATION INTERRUPTED (EXAMPLE)", 12, accent, false));

        var footer = new StackPanel { Spacing = 7 };
        footer.Children.Add(new Rectangle { Height = 3, Fill = accent, Opacity = 0.85 });
        footer.Children.Add(Label("SHOWCASE ONLY  //  NO FILES OR DATA ARE MODIFIED", 10,
            Brush(162, 178, 185), false));
        stack.Children.Add(footer);

        var outer = new Grid { Width = panelWidth, MaxWidth = 760 };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rail = new StackPanel { Background = dim, Spacing = 8, Padding = new Thickness(3, 12, 3, 12) };
        for (var i = 0; i < 8; i++)
            rail.Children.Add(new Rectangle { Height = 19, Fill = accent, Opacity = i % 2 == 0 ? 1 : 0.45 });
        outer.Children.Add(rail);
        var body = new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            Background = Brush(10, 14, 18),
            Padding = new Thickness(18),
            Child = stack
        };
        Grid.SetColumn(body, 1);
        outer.Children.Add(body);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ElementTheme.Dark,
            Title = null,
            Content = outer,
            PrimaryButtonText = "NEXT PREVIEW",
            CloseButtonText = "CLOSE SHOWCASE",
            DefaultButton = ContentDialogButton.Close,
            Background = Brush(7, 10, 14),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Width = dialogWidth,
            MaxWidth = dialogWidth
        };
        // Override this specimen only, preserving all production dialogs.
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth + 24d;
        dialog.Resources["ContentDialogMinWidth"] = Math.Min(dialogWidth, 400d);
        if (Application.Current.Resources.TryGetValue("HiTechPrimaryButtonStyle", out var primary) && primary is Style ps)
            dialog.PrimaryButtonStyle = ps;
        if (Application.Current.Resources.TryGetValue("HiTechIconButtonStyle", out var close) && close is Style cs)
            dialog.CloseButtonStyle = cs;
        return dialog;
    }

    private static TextBlock Label(string value, double size, SolidColorBrush color, bool bold) =>
        new()
        {
            Text = value,
            FontFamily = TechnicalFont,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = color,
            TextWrapping = TextWrapping.Wrap
        };
}
