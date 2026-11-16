using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Text;

namespace PhotoLibrarian.Views;

/// <summary>
/// Shared cinematic presentation for previews and confirmations.
/// This class never performs any filesystem, database or album operations.
/// </summary>
internal static class CinematicDialogShowcase
{
    internal enum Variant { Fatal, Error, Warning, Complete, Information, Progress }

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(255, r, g, b));

    private static readonly FontFamily TechnicalFont = new("Consolas");

    internal static ContentDialog Create(XamlRoot root, Variant variant,
        string? subjectOverride = null, string? detailOverride = null,
        string? confirmLabel = null, bool preview = true, string? context = null,
        string? captionOverride = null, string? operationLabel = null)
    {
        var fatal = variant == Variant.Fatal;
        var warning = variant == Variant.Warning;
        var error = variant == Variant.Error;
        var complete = variant == Variant.Complete;
        var progress = variant == Variant.Progress;
        var critical = fatal || error;
        var accent = critical ? Brush(248, 43, 55) : warning ? Brush(255, 191, 40)
            : complete ? Brush(64, 204, 211) : progress ? Brush(84, 161, 206) : Brush(154, 176, 191);
        var dim = critical ? Brush(47, 11, 15) : warning ? Brush(43, 31, 10)
            : complete ? Brush(9, 37, 41) : progress ? Brush(14, 29, 42) : Brush(24, 29, 37);
        var caption = variant switch
        {
            Variant.Fatal => "SYSTEM ALERT // E-1047",
            Variant.Error => "SYSTEM ERROR // E-041",
            Variant.Warning => "USER CONFIRMATION // ALBUM",
            Variant.Complete => "SYSTEM STATUS // SCAN",
            Variant.Progress => "SYSTEM PROCESS // INDEXING",
            _ => "SYSTEM MESSAGE // INFO"
        };
        var subject = variant switch
        {
            Variant.Fatal => "DATABASE INTEGRITY FAILURE",
            Variant.Error => "METADATA WRITE FAILED",
            Variant.Warning => "DELETE SELECTED ALBUM?",
            Variant.Complete => "ALBUM SCAN FINISHED",
            Variant.Progress => "INDEXING ALBUM CONTENT",
            _ => "ALBUM SETTINGS"
        };
        var detail = variant switch
        {
            Variant.Fatal => "The album database cannot be accessed safely. The current operation was stopped to protect your data.",
            Variant.Error => "The metadata file could not be updated because another application is using it. The affected file was not modified.",
            Variant.Warning => "This is a visual demonstration only. In the real application, deleting an album requires explicit confirmation.",
            Variant.Complete => "This example represents a successfully completed album scan. No actual files were scanned.",
            Variant.Progress => "This is a static processing layout preview. No indexing is running and the counters below are examples.",
            _ => "This panel demonstrates an ordinary system message without an alarm. No settings are changed."
        };
        subject = subjectOverride ?? subject;
        detail = detailOverride ?? detail;
        var glyph = critical || warning ? "\uE7BA" : complete ? "\uE73E"
            : progress ? "\uE895" : "\uE946";
        // WinUI centers its ContentDialog through its own presenter/template.
        // Setting Width or MaxWidth on the ContentDialog itself prevents
        // recentering (microsoft/microsoft-ui-xaml#424). Resize the CONTENT,
        // and raise only the dialog template's width resource instead.
        var availableWidth = Math.Max(320, root.Size.Width - 64);
        var dialogWidth = Math.Min(800, availableWidth);
        var panelWidth = dialogWidth - 80;
        var compact = dialogWidth < 660;
        var heroFont = compact ? (dialogWidth < 500 ? 39 : 49) : 72;
        var warningFont = compact ? (dialogWidth < 500 ? 35 : 43) : 64;

        var stack = new StackPanel { Spacing = 13 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = Label(captionOverride ?? caption, 11, accent, true);
        header.Children.Add(headerText);
        var safe = Label(preview ? "PREVIEW ONLY" : (operationLabel ?? "ALBUM OPERATION"),
            10, Brush(153, 169, 177), false);
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
        else if (error)
            titleStack.Children.Add(Label("ERROR", heroFont, accent, true));
        else if (complete)
        {
            titleStack.Children.Add(Label("PROCESS", compact ? 32 : 52, accent, true));
            titleStack.Children.Add(Label("COMPLETE", compact ? 32 : 52, accent, true));
        }
        else if (progress)
            titleStack.Children.Add(Label("PROCESSING", compact ? 30 : 50, accent, true));
        else
            titleStack.Children.Add(Label("INFORMATION", compact ? 29 : 48, accent, true));
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

        if (context is not null || (warning && preview))
        {
            stack.Children.Add(new Border
            {
                Background = Brush(25, 29, 31),
                Padding = new Thickness(12),
                Child = Label("TARGET  //  " + (context ?? "Family Photos (example)"), 13, Brush(230, 233, 236), false)
            });
        }
        else if (complete && preview)
            stack.Children.Add(Label("PROCESSED  12,846    |    UPDATED  12,846    |    ERRORS  0   (EXAMPLE)", 12, accent, false));
        else if (progress && preview)
        {
            var track = new Grid { Height = 12, Background = Brush(28, 43, 52) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(63, GridUnitType.Star) });
            track.Children.Add(new Border { Background = accent });
            stack.Children.Add(track);
            stack.Children.Add(Label("PREVIEW  //  37% EXAMPLE  //  NO ACTIVE PROCESS", 12, accent, false));
        }
        else if (preview && (error || fatal))
            stack.Children.Add(Label("SYSTEM STATE  //  OPERATION INTERRUPTED (EXAMPLE)", 12, accent, false));
        else if (preview)
            stack.Children.Add(Label("STATUS  //  INFORMATION ONLY", 12, accent, false));

        var footer = new StackPanel { Spacing = 7 };
        footer.Children.Add(new Rectangle { Height = 3, Fill = accent, Opacity = 0.85 });
        footer.Children.Add(Label(preview ? "SHOWCASE ONLY  //  NO FILES OR DATA ARE MODIFIED" : "REVIEW THE OPERATION BEFORE CONFIRMING", 10,
            Brush(162, 178, 185), false));
        stack.Children.Add(footer);

        var outer = new Grid
        {
            Width = panelWidth,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Center
        };
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
            Content = new ScrollViewer
            {
                Content = outer,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(260, root.Size.Height - 160),
                HorizontalAlignment = HorizontalAlignment.Center
            },
            PrimaryButtonText = preview ? "NEXT PREVIEW" : confirmLabel,
            CloseButtonText = preview ? "CLOSE SHOWCASE" :
                string.IsNullOrWhiteSpace(confirmLabel)
                    ? (critical ? "ACKNOWLEDGE" : "CLOSE")
                    : "CANCEL",
            // Avoid WinUI's blue accent-default button overriding our severity styles.
            // ESC and the explicit CANCEL button still dismiss safely; no destructive
            // action is selected automatically by Enter.
            DefaultButton = ContentDialogButton.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush(7, 10, 14),
            BorderBrush = accent,
            BorderThickness = new Thickness(1)
        };
        // Override this specimen only, preserving all production dialogs.
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth + 24d;
        dialog.Resources["ContentDialogMinWidth"] = Math.Min(dialogWidth, 320d);
        // Preview controls belong to the same severity palette as the panel.
        // Avoid the default blue WinUI accent, without changing app-wide styles.
        dialog.PrimaryButtonStyle = ActionStyle(accent, dim, solid: !preview);
        // Cancel/dismiss is neutral rather than a second bright colored action.
        dialog.CloseButtonStyle = ActionStyle(Brush(91, 120, 136), Brush(17, 34, 48), solid: false);
        return dialog;
    }

    private static Style ActionStyle(SolidColorBrush accent, SolidColorBrush dim, bool solid)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, solid ? accent : dim));
        style.Setters.Add(new Setter(Button.ForegroundProperty,
            solid ? Brush(7, 11, 16) : Brush(239, 243, 246)));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, accent));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(0)));
        style.Setters.Add(new Setter(Button.FontFamilyProperty, TechnicalFont));
        style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Bold));
        style.Setters.Add(new Setter(Button.MinHeightProperty, 42d));
        return style;
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
