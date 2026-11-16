using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoLibrarian.Controls;

/// <summary>
/// Shared progress indicator matching the BootWindow's original progress bar.
/// A single component serves known percentages and indeterminate operations.
/// IsActive collapses idle progress just like the old ProgressRing.
/// </summary>
public sealed partial class CinematicProgressBar : UserControl
{
    public CinematicProgressBar()
    {
        InitializeComponent();
        SyncBar();
        UpdateActivity();
    }

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double),
            typeof(CinematicProgressBar), new PropertyMetadata(0d, OnProgressChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double),
            typeof(CinematicProgressBar), new PropertyMetadata(100d, OnProgressChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double),
            typeof(CinematicProgressBar), new PropertyMetadata(0d, OnProgressChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool),
            typeof(CinematicProgressBar), new PropertyMetadata(false, OnProgressChanged));

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool),
            typeof(CinematicProgressBar), new PropertyMetadata(true, OnActiveChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnProgressChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((CinematicProgressBar)dependencyObject).SyncBar();

    private static void OnActiveChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((CinematicProgressBar)dependencyObject).UpdateActivity();

    private void SyncBar()
    {
        if (InnerProgress is null) return;
        // Apply range before Value, as in the native ProgressBar contract.
        InnerProgress.Minimum = Minimum;
        InnerProgress.Maximum = Maximum;
        InnerProgress.Value = Value;
        InnerProgress.IsIndeterminate = IsIndeterminate;
    }

    private void UpdateActivity() =>
        Visibility = IsActive ? Visibility.Visible : Visibility.Collapsed;
}
