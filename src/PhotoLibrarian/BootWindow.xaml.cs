using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace PhotoLibrarian;

public sealed partial class BootWindow : Window
{
    public BootWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 430));
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
        string detail)
    {
        PhaseCodeText.Text = $"BOOT // {phase:00}";
        StatusText.Text = status;
        DetailText.Text = detail;
        BootProgress.Value = percent;
        PercentText.Text = $"{percent:000}%";
    }
}
