using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoLibrarian.Views;

/// <summary>
/// Presentation-only defaults for ordinary WinUI dialogs. The cinematic
/// severity dialogs keep their own palette. No button result, content,
/// handlers, confirmation rules or dialog lifetime is changed here.
/// </summary>
internal static class CinematicDialogStyling
{
    internal static void Apply(ContentDialog dialog, bool destructive = false)
    {
        dialog.HorizontalAlignment = HorizontalAlignment.Center;
        dialog.VerticalAlignment = VerticalAlignment.Center;
        dialog.RequestedTheme = ElementTheme.Dark;

        // The WinUI default Close button would otherwise receive a blue
        // AccentButton treatment and override its neutral cinematic style.
        // ESC and the explicit Close/Cancel command remain available.
        if (dialog.DefaultButton == ContentDialogButton.Close)
            dialog.DefaultButton = ContentDialogButton.None;

        var resources = Application.Current.Resources;
        if (resources.TryGetValue(
                destructive ? "HiTechDangerButtonStyle" : "HiTechPrimaryButtonStyle",
                out var primary) && primary is Style primaryStyle)
            dialog.PrimaryButtonStyle = primaryStyle;

        if (resources.TryGetValue("HiTechSectionActionStyle", out var neutral)
            && neutral is Style neutralStyle)
        {
            dialog.SecondaryButtonStyle = neutralStyle;
            dialog.CloseButtonStyle = neutralStyle;
        }
    }
}
