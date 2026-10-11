using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace PhotoLibrarian;

public sealed partial class BootWindow : Window
{
    public BootWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 430));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var bounds = area.WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            bounds.X + System.Math.Max(0, (bounds.Width - AppWindow.Size.Width) / 2),
            bounds.Y + System.Math.Max(0, (bounds.Height - AppWindow.Size.Height) / 2)));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(BootDragRegion);
        if (AppWindow.TitleBar is { } titleBar)
        {
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 7, 11, 16);
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 7, 11, 16);
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 83, 215, 232);
        }
        AppWindow.Title = "PhotoLibrarian // Boot";

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        try
        {
            var iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "PhotoLibrarian.ico");
            if (System.IO.File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch
        {
        }
    }

    public void SetPhase(
        int phase,
        int percent,
        string status,
        string detail,
        bool warning = false,
        bool fatal = false)
    {
        var alert = fatal
            ? "FATAL ERROR"
            : warning
                ? "WARNING // ARCHIVE CONTEXT CHANGE"
                : string.Empty;

        AlertText.Text = alert;
        AlertText.Visibility = string.IsNullOrEmpty(alert)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var activeColor = fatal
            ? Windows.UI.Color.FromArgb(255, 255, 52, 64)
            : warning
                ? Windows.UI.Color.FromArgb(255, 255, 90, 100)
                : Windows.UI.Color.FromArgb(255, 83, 215, 232);

        var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(activeColor);
        AlertText.Foreground = brush;
        BootProgress.Foreground = brush;
        StatusText.Foreground = fatal || warning
            ? brush
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 230, 247, 250));

        PhaseCodeText.Text = $"BOOT // {phase:00}";
        StatusText.Text = status;
        DetailText.Text = detail;
        BootProgress.Value = percent;
        PercentText.Text = $"{percent:000}%";
        PercentText.Foreground = brush;
    }
}
