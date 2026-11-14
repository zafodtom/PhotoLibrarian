using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoLibrarian.ViewModels;
using System;
using System.Globalization;
using System.Linq;

namespace PhotoLibrarian.Views;

public sealed partial class MetadataPanel : UserControl
{
    private MetadataPanelViewModel? ViewModel => App.ViewModel?.MetadataPanel;

    private bool _suppressCaptionSave;
    private string _lastLoadedCaption = "";
    private bool _suppressDateBoxLostFocus;
    private string _lastLoadedDateText = "";
    private bool _suppressTagSuggestions;
    private const int RecentTagLimit = 5;
    private static readonly string RecentTagsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoLibrarian", "recent-tags");
    private string? _recentTagsAlbumPath;
    private readonly ObservableCollection<string> _recentTags = new();
    private readonly ObservableCollection<string> _visibleRecentTags = new();

    // Accepted date formats (in order). M/d/yyyy h:mm tt is the canonical one shown to user.
    private static readonly string[] AcceptedDateFormats = new[]
    {
        "M/d/yyyy h:mm tt",
        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy H:mm",
        "M/d/yyyy H:mm:ss",
        "M/d/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss"
    };

    public MetadataPanel()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        TagsList.ItemsSource = ViewModel.Tags;
        RecentTagsList.ItemsSource = _visibleRecentTags;
        ViewModel.Tags.CollectionChanged += OnSelectedTagsChanged;
        EnsureRecentTagsAlbum();
    }

    private void OnSelectedTagsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshRecentTagsVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MetadataPanelViewModel.HasImage):
            case nameof(MetadataPanelViewModel.FileName):
            case nameof(MetadataPanelViewModel.SelectionCount):
                DispatcherQueue.TryEnqueue(UpdateDisplay);
                break;
            case nameof(MetadataPanelViewModel.CommonDateTaken):
            case nameof(MetadataPanelViewModel.IsDateMixed):
                DispatcherQueue.TryEnqueue(RefreshDateDisplay);
                break;
        }
    }

    private void UpdateDisplay()
    {
        EnsureRecentTagsAlbum();
        if (ViewModel is null || !ViewModel.HasImage)
        {
            EmptyContent.Visibility = Visibility.Visible;
            DetailContent.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyContent.Visibility = Visibility.Collapsed;
        DetailContent.Visibility = Visibility.Visible;

        RefreshRecentTagsVisibility();

        // Multi-select banner
        bool multi = ViewModel.IsMultiSelect;
        if (multi)
        {
            MultiSelectBanner.Visibility = Visibility.Visible;
            MultiSelectText.Text = $"{ViewModel.SelectionCount} items selected — edits apply to all";
            DateMultiHint.Visibility = ViewModel.IsDateMixed ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            MultiSelectBanner.Visibility = Visibility.Collapsed;
            DateMultiHint.Visibility = Visibility.Collapsed;
            // Always start in absolute mode for single-select
            DateAbsoluteMode.Visibility = Visibility.Visible;
            DateShiftMode.Visibility = Visibility.Collapsed;
        }
        // Shift link is useful both for time-zone fixes (multi) and camera-clock fixes (single)
        SwitchToShiftBtn.Visibility = ViewModel.CommonDateTaken.HasValue || ViewModel.IsDateMixed
            ? Visibility.Visible : Visibility.Collapsed;

        // Caption
        _suppressCaptionSave = true;
        CaptionBox.Text = ViewModel.Caption ?? "";
        _lastLoadedCaption = CaptionBox.Text;
        CaptionBox.PlaceholderText = ViewModel.IsCaptionMixed
            ? "Multiple captions — type to replace all"
            : "Add caption";
        CaptionMixedHint.Visibility = ViewModel.IsCaptionMixed ? Visibility.Visible : Visibility.Collapsed;
        _suppressCaptionSave = false;

        // Date taken — compact text editor
        RefreshDateDisplay();

        // Geotag
        if (!string.IsNullOrEmpty(ViewModel.GpsLatitude) && ViewModel.GpsLatitude != "Multiple values")
            GeotagText.Text = $"{ViewModel.GpsLatitude}, {ViewModel.GpsLongitude}";
        else if (ViewModel.GpsLatitude == "Multiple values")
            GeotagText.Text = "Multiple geotags";
        else
            GeotagText.Text = "Add geotag";

        // Information section
        InfoFileName.Text = ViewModel.FileName;
        InfoFolder.Text = !string.IsNullOrEmpty(ViewModel.FolderPath) ? ViewModel.FolderPath : "—";
        InfoFileSize.Text = ViewModel.FileSize;
        InfoDimensions.Text = !string.IsNullOrEmpty(ViewModel.Dimensions) ? ViewModel.Dimensions : "—";
        InfoCamera.Text = !string.IsNullOrEmpty(ViewModel.Camera) ? ViewModel.Camera : "—";
        InfoAuthor.Text = !string.IsNullOrEmpty(ViewModel.Author) ? ViewModel.Author : "Add an author";
        InfoExposure.Text = ViewModel.Exposure;
        InfoAperture.Text = ViewModel.Aperture;
        InfoFocalLength.Text = ViewModel.FocalLength;
        InfoIso.Text = ViewModel.Iso;
        InfoLatitude.Text = ViewModel.GpsLatitude;
        InfoLongitude.Text = ViewModel.GpsLongitude;
        InfoFilePath.Text = ViewModel.FilePath;

        InfoCameraRow.Visibility = string.IsNullOrEmpty(ViewModel.Camera) ? Visibility.Collapsed : Visibility.Visible;
        InfoExposureRow.Visibility = string.IsNullOrEmpty(ViewModel.Exposure) ? Visibility.Collapsed : Visibility.Visible;
        InfoApertureRow.Visibility = string.IsNullOrEmpty(ViewModel.Aperture) ? Visibility.Collapsed : Visibility.Visible;
        InfoFocalLengthRow.Visibility = string.IsNullOrEmpty(ViewModel.FocalLength) ? Visibility.Collapsed : Visibility.Visible;
        InfoIsoRow.Visibility = string.IsNullOrEmpty(ViewModel.Iso) ? Visibility.Collapsed : Visibility.Visible;
        InfoLatRow.Visibility = Visibility.Collapsed;
        InfoLonRow.Visibility = Visibility.Collapsed;
        InfoDimensionsRow.Visibility = string.IsNullOrEmpty(ViewModel.Dimensions) ? Visibility.Collapsed : Visibility.Visible;

    }

    // --- Caption ---

    private void OnCaptionLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || _suppressCaptionSave) return;
        if (CaptionBox.Text == _lastLoadedCaption) return;
        ViewModel.Caption = CaptionBox.Text;
        ViewModel.SaveCaptionCommand.Execute(null);
        _lastLoadedCaption = CaptionBox.Text;
        CaptionMixedHint.Visibility = Visibility.Collapsed;
    }

    // --- Tags ---

    private void OnAddTagClick(object sender, RoutedEventArgs e) => AddCurrentTag();

    private void OnShowAllTagsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        ShowTagSuggestions(ViewModel.AvailableTags);
        NewTagBox.Focus(FocusState.Programmatic);
    }

    private void OnTagTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTagSuggestions || ViewModel is null) return;

        var query = NewTagBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            HideTagSuggestions();
            return;
        }

        var matches = ViewModel.AvailableTags
            .Where(item => item.Tag.Contains(query, StringComparison.OrdinalIgnoreCase))
            
            .ToList();

        if (matches.Count == 0)
        {
            HideTagSuggestions();
            return;
        }

        ShowTagSuggestions(matches);
    }

    private void OnTagSuggestionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AvailableTagItem chosen) return;

        _suppressTagSuggestions = true;
        NewTagBox.Text = chosen.Tag;
        _suppressTagSuggestions = false;

        AddCurrentTag();
    }

    // Shared dark HUD host for inline tag dialogs. Keep native ContentDialog
    // buttons and result handling; only the presentation is customized.
    private static Border CreateTagDialogHost(StackPanel fields, string heading)
    {
        var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 83, 215, 232));
        var muted = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 140, 169, 179));
        var title = new TextBlock
        {
            Text = heading,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            CharacterSpacing = 100,
            Foreground = accent
        };
        var caption = new TextBlock
        {
            Text = "HIERARCHY  //  CATALOG CONTROL",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = muted
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 390 };
        content.Children.Add(title);
        content.Children.Add(caption);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 36, 66, 82))
        });
        content.Children.Add(fields);
        return new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 18, 26)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 115, 138)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = content
        };
    }

    private async void OnNewCatalogTagClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        HideTagSuggestions();

        var parentTags = ViewModel.AvailableTags
            .Select(item => item.Tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parentBox = new ComboBox
        {
            Header = "Parent tag",
            PlaceholderText = "Catalog root",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        parentBox.Items.Add("(root)");
        foreach (var parentTag in parentTags)
            parentBox.Items.Add(parentTag);
        parentBox.SelectedIndex = 0;

        var nameBox = new TextBox
        {
            Header = "Tag name",
            PlaceholderText = "For example: Red"
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(parentBox);
        panel.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = null,
            Content = CreateTagDialogHost(panel, "NEW CATALOG TAG"),
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 9, 16, 24)),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 247, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 115, 138)),
            BorderThickness = new Thickness(1)
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text?.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(name))
            return;

        var parent = parentBox.SelectedIndex > 0
            ? parentBox.SelectedItem?.ToString()
            : null;
        var fullTag = string.IsNullOrWhiteSpace(parent)
            ? name
            : $"{parent}/{name}";

        var tagValidation = ViewModel.ValidateCatalogPath(fullTag);
        if (tagValidation is not null)
        {
            App.ViewModel.StatusText = tagValidation;
            return;
        }

        AlbumService.AddSelectedTag(fullTag);
        await ViewModel.ReloadAvailableTagsAsync();
        ShowTagSuggestions(ViewModel.AvailableTags);
    }

    private async void OnManageCatalogItemClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null ||
            sender is not Button { Tag: AvailableTagItem item })
            return;

        HideTagSuggestions();

        var parentTags = ViewModel.AvailableTags
            .Where(candidate =>
                !string.Equals(candidate.Tag, item.Tag, StringComparison.OrdinalIgnoreCase) &&
                !candidate.Tag.StartsWith(item.Tag + "/", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentParent = "";
        var slash = item.Tag.LastIndexOf('/');
        if (slash > 0)
            currentParent = item.Tag[..slash];

        var parentBox = new ComboBox
        {
            Header = "Parent tag",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        parentBox.Items.Add("(root)");
        foreach (var parentTag in parentTags)
            parentBox.Items.Add(parentTag);

        parentBox.SelectedIndex = 0;
        for (var index = 0; index < parentTags.Count; index++)
        {
            if (string.Equals(parentTags[index], currentParent, StringComparison.OrdinalIgnoreCase))
            {
                parentBox.SelectedIndex = index + 1;
                break;
            }
        }

        var nameBox = new TextBox
        {
            Header = "Tag name",
            Text = item.Name
        };

        var info = new TextBlock
        {
            Text = item.IsUsedInAlbum
                ? "Renaming also updates photos that use this tag."
                : "This tag is not currently used by any photo.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(
                Microsoft.UI.Colors.Gray)
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(parentBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(info);

        var hudContent = new StackPanel { Spacing = 12, MinWidth = 420 };
        hudContent.Children.Add(CreateTagDialogHost(panel, "MANAGE CATALOG TAG"));
        var actionRail = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var saveButton = new Button { Content = "SAVE CHANGES" };
        var removeButton = new Button { Content = "REMOVE FROM CATALOG" };
        var cancelButton = new Button { Content = "CANCEL" };
        saveButton.Style = (Style)Application.Current.Resources["HiTechPrimaryButtonStyle"];
        removeButton.Style = (Style)Application.Current.Resources["HiTechIconButtonStyle"];
        cancelButton.Style = (Style)Application.Current.Resources["HiTechIconButtonStyle"];
        actionRail.Children.Add(saveButton);
        actionRail.Children.Add(removeButton);
        actionRail.Children.Add(cancelButton);
        hudContent.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 115, 138)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            Child = actionRail
        });
        var dialog = new ContentDialog
        {
            Title = null,
            Content = hudContent,
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 9, 16, 24)),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 247, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 115, 138)),
            BorderThickness = new Thickness(1)
        };

        var result = ContentDialogResult.None;
        saveButton.Click += (_, _) => { result = ContentDialogResult.Primary; dialog.Hide(); };
        removeButton.Click += (_, _) => { result = ContentDialogResult.Secondary; dialog.Hide(); };
        cancelButton.Click += (_, _) => dialog.Hide();
        await dialog.ShowAsync();

        if (result == ContentDialogResult.Secondary)
        {
            await ViewModel.RemoveCatalogItemAsync(item);
            ShowTagSuggestions(ViewModel.AvailableTags);
            return;
        }

        if (result != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text?.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(name))
            return;

        var parent = parentBox.SelectedIndex > 0
            ? parentBox.SelectedItem?.ToString()
            : null;

        var newFullPath = string.IsNullOrWhiteSpace(parent)
            ? name
            : $"{parent}/{name}";

        var pathValidation = ViewModel.ValidateCatalogPath(newFullPath, item.Tag);
        if (pathValidation is not null)
        {
            App.ViewModel.StatusText = pathValidation;
            return;
        }

        try
        {
            await ViewModel.RenameCatalogItemAsync(item, newFullPath);
            App.ViewModel.StatusText =
                $"Tag přejmenován na '{newFullPath}'.";
        }
        catch (Exception ex)
        {
            App.ViewModel.StatusText = $"Přejmenování se nezdařilo: {ex.Message}";
        }

        ShowTagSuggestions(ViewModel.AvailableTags);
    }

    private async void OnToggleAlbumTagSelectionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null ||
            sender is not Button { Tag: AvailableTagItem item })
            return;

        await ViewModel.ToggleAlbumTagSelectionAsync(item);

        // Keep the picker open and refresh its current contents after toggling.
        var query = NewTagBox.Text?.Trim() ?? "";
        var visible = string.IsNullOrEmpty(query)
            ? ViewModel.AvailableTags.ToList()
            : ViewModel.AvailableTags
                .Where(tag => tag.Tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                
                .ToList();

        ShowTagSuggestions(visible);
    }

    private void OnNewTagKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            AddCurrentTag();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideTagSuggestions();
            e.Handled = true;
        }
    }

    private async void AddCurrentTag()
    {
        var tag = NewTagBox.Text?.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        await AssignAndRememberTagAsync(tag);
        if (!string.Equals(NewTagBox.Text?.Trim(), tag, StringComparison.Ordinal)) return;
        _suppressTagSuggestions = true;
        NewTagBox.Text = "";
        _suppressTagSuggestions = false;
        HideTagSuggestions();
    }

    private async void OnRecentTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            await AssignAndRememberTagAsync(tag);
    }

    private async Task AssignAndRememberTagAsync(string tag)
    {
        EnsureRecentTagsAlbum();
        var viewModel = ViewModel;
        var albumPath = _recentTagsAlbumPath;
        if (viewModel is null || !viewModel.HasImage || albumPath is null) return;
        await viewModel.AddTagAsync(tag);
        // A long-running write must not update another album's history.
        EnsureRecentTagsAlbum();
        if (string.Equals(albumPath, _recentTagsAlbumPath, StringComparison.OrdinalIgnoreCase))
            RememberTag(tag);
    }

    private static string? GetAlbumRecentTagsFile(string? albumPath)
    {
        if (string.IsNullOrWhiteSpace(albumPath)) return null;
        var normalized = Path.GetFullPath(albumPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(RecentTagsDirectory, hash + ".json");
    }

    private void EnsureRecentTagsAlbum()
    {
        var album = App.HasActiveAlbum ? App.CurrentAlbumPath : null;
        if (string.Equals(album, _recentTagsAlbumPath, StringComparison.OrdinalIgnoreCase))
            return;

        _recentTagsAlbumPath = album;
        _recentTags.Clear();
        try
        {
            var file = GetAlbumRecentTagsFile(album);
            if (file is not null && File.Exists(file))
            {
                var saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file));
                foreach (var tag in (saved ?? Array.Empty<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(RecentTagLimit))
                    _recentTags.Add(tag.Trim());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RECENT TAGS] Album history load failed: {ex}");
        }
        RefreshRecentTagsVisibility();
    }

    private void RememberTag(string tag)
    {
        EnsureRecentTagsAlbum();
        tag = tag.Trim();
        if (tag.Length == 0 || _recentTagsAlbumPath is null) return;
        for (var i = _recentTags.Count - 1; i >= 0; i--)
            if (string.Equals(_recentTags[i], tag, StringComparison.OrdinalIgnoreCase))
                _recentTags.RemoveAt(i);
        _recentTags.Insert(0, tag);
        while (_recentTags.Count > RecentTagLimit)
            _recentTags.RemoveAt(_recentTags.Count - 1);
        RefreshRecentTagsVisibility();
        try
        {
            var file = GetAlbumRecentTagsFile(_recentTagsAlbumPath);
            if (file is null) return;
            Directory.CreateDirectory(RecentTagsDirectory);
            File.WriteAllText(file, JsonSerializer.Serialize(_recentTags.ToArray()));
        }
        catch (Exception ex)
        {
            // Tag assignment must not fail merely because history cannot be saved.
            System.Diagnostics.Debug.WriteLine($"[RECENT TAGS] Unable to save history: {ex}");
        }
    }

    private void RefreshRecentTagsVisibility()
    {
        _visibleRecentTags.Clear();
        var model = ViewModel;
        if (model?.HasImage == true)
        {
            // A quick-selection tag is useful only if at least one selected
            // image does not already have that explicit assignment.
            var fullyAssigned = model.Tags
                .Where(t => t.IsOnAll)
                .Select(t => t.Tag)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _recentTags)
                if (!fullyAssigned.Contains(tag))
                    _visibleRecentTags.Add(tag);
        }

        RecentTagsPanel.Visibility = _visibleRecentTags.Count > 0 &&
            TagSuggestionsPanel.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowTagSuggestions(IEnumerable<AvailableTagItem> tags)
    {
        var items = tags.ToList();
        if (items.Count == 0)
        {
            HideTagSuggestions();
            return;
        }

        TagSuggestionsList.ItemsSource = items;
        TagSuggestionsPanel.Visibility = Visibility.Visible;
        RefreshRecentTagsVisibility();
    }

    private void HideTagSuggestions()
    {
        TagSuggestionsPanel.Visibility = Visibility.Collapsed;
        RefreshRecentTagsVisibility();
    }

    private void OnRemoveTagClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button btn) return;
        var tag = btn.Tag as string;
        if (!string.IsNullOrEmpty(tag))
            ViewModel.RemoveTagCommand.Execute(tag);
    }

    // --- Date taken: compact text editor ---

    /// <summary>
    /// Re-renders the date editor from the current ViewModel state. Called whenever
    /// CommonDateTaken / IsDateMixed change (e.g. after Apply or Shift) so the box
    /// reflects the new value without forcing a full UpdateDisplay rebuild.
    /// </summary>
    private void RefreshDateDisplay()
    {
        if (ViewModel is null) return;

        _suppressDateBoxLostFocus = true;
        if (ViewModel.IsDateMixed)
        {
            DateTakenBox.Text = "";
            DateTakenBox.PlaceholderText = "Multiple dates — type to set all";
            DateMixedHint.Visibility = Visibility.Visible;
            DateMultiHint.Visibility = Visibility.Collapsed;
        }
        else if (ViewModel.CommonDateTaken.HasValue)
        {
            var d = ViewModel.CommonDateTaken.Value;
            DateTakenBox.Text = d.LocalDateTime.ToString("M/d/yyyy h:mm tt", CultureInfo.CurrentCulture);
            DateTakenBox.PlaceholderText = "M/d/yyyy h:mm tt";
            DateMixedHint.Visibility = Visibility.Collapsed;
            if (ViewModel.IsMultiSelect)
                DateMultiHint.Visibility = Visibility.Visible;
        }
        else
        {
            DateTakenBox.Text = "";
            DateTakenBox.PlaceholderText = "M/d/yyyy h:mm tt";
            DateMixedHint.Visibility = Visibility.Collapsed;
        }
        _lastLoadedDateText = DateTakenBox.Text;
        DateParseError.Visibility = Visibility.Collapsed;

        // Pre-fill flyout pickers from current date if any
        var seed = ViewModel.CommonDateTaken ?? DateTimeOffset.Now;
        DatePopupCal.Date = new DateTimeOffset(seed.Year, seed.Month, seed.Day, 0, 0, 0, seed.Offset);
        DatePopupTime.Time = new TimeSpan(seed.Hour, seed.Minute, seed.Second);
        _suppressDateBoxLostFocus = false;

        // If shift mode is currently open, refresh its preview against the new common date
        if (DateShiftMode.Visibility == Visibility.Visible)
            UpdateShiftPreview();
    }

    private async void OnDateTakenBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || _suppressDateBoxLostFocus) return;
        if (DateTakenBox.Text == _lastLoadedDateText) return;
        await CommitDateTextAsync();
    }

    private async void OnDateTakenBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await CommitDateTextAsync();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            // Revert
            DateTakenBox.Text = _lastLoadedDateText;
            DateParseError.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task CommitDateTextAsync()
    {
        if (ViewModel is null) return;

        var txt = DateTakenBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(txt))
        {
            // Empty — silently ignore; user can clear via flyout if desired
            DateParseError.Visibility = Visibility.Collapsed;
            return;
        }

        if (!TryParseDate(txt, out var parsed))
        {
            DateParseError.Visibility = Visibility.Visible;
            return;
        }

        DateParseError.Visibility = Visibility.Collapsed;
        await ViewModel.SetDateTakenAsync(parsed);
        _lastLoadedDateText = parsed.ToString("M/d/yyyy h:mm tt", CultureInfo.CurrentCulture);
        // Re-format the box to canonical form
        _suppressDateBoxLostFocus = true;
        DateTakenBox.Text = _lastLoadedDateText;
        _suppressDateBoxLostFocus = false;
    }

    private static bool TryParseDate(string text, out DateTime result)
    {
        if (DateTime.TryParseExact(text, AcceptedDateFormats, CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal, out result))
            return true;
        // Last-ditch general parse
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result);
    }

    private async void OnApplyDatePopupClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (DatePopupCal.Date is not DateTimeOffset date) return;
        var time = DatePopupTime.Time;
        var combined = new DateTime(date.Year, date.Month, date.Day,
                                    time.Hours, time.Minutes, time.Seconds,
                                    DateTimeKind.Local);
        await ViewModel.SetDateTakenAsync(combined);
        // Close the flyout
        DatePickerBtn.Flyout?.Hide();
    }

    private async void OnApplyShiftClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var offset = BuildShiftOffset();
        if (offset == TimeSpan.Zero) return;

        ApplyShiftButton.IsEnabled = false;
        try
        {
            await ViewModel.OffsetDateTakenAsync(offset);
        }
        finally
        {
            ApplyShiftButton.IsEnabled = true;
        }

        ResetShiftFields();
        // Switch back to absolute view; RefreshDateDisplay is triggered by VM property change.
        DateShiftMode.Visibility = Visibility.Collapsed;
        DateAbsoluteMode.Visibility = Visibility.Visible;
    }

    private void OnSwitchToShift(object sender, RoutedEventArgs e)
    {
        ResetShiftFields();
        UpdateShiftHeader();
        UpdateShiftPreview();
        DateAbsoluteMode.Visibility = Visibility.Collapsed;
        DateShiftMode.Visibility = Visibility.Visible;
    }

    private void OnSwitchToAbsolute(object sender, RoutedEventArgs e)
    {
        DateShiftMode.Visibility = Visibility.Collapsed;
        DateAbsoluteMode.Visibility = Visibility.Visible;
    }

    private void OnShiftValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NumberBox raises ValueChanged during XAML init (Value="0") before all siblings exist.
        if (ShiftDaysBox is null || ShiftHoursBox is null
            || ShiftMinutesBox is null || ShiftSecondsBox is null) return;
        UpdateShiftPreview();
    }

    private TimeSpan BuildShiftOffset()
    {
        if (ShiftDaysBox is null || ShiftHoursBox is null
            || ShiftMinutesBox is null || ShiftSecondsBox is null) return TimeSpan.Zero;
        int days = double.IsNaN(ShiftDaysBox.Value) ? 0 : (int)ShiftDaysBox.Value;
        int hours = double.IsNaN(ShiftHoursBox.Value) ? 0 : (int)ShiftHoursBox.Value;
        int minutes = double.IsNaN(ShiftMinutesBox.Value) ? 0 : (int)ShiftMinutesBox.Value;
        int seconds = double.IsNaN(ShiftSecondsBox.Value) ? 0 : (int)ShiftSecondsBox.Value;
        return new TimeSpan(days, hours, minutes, seconds);
    }

    private void ResetShiftFields()
    {
        ShiftDaysBox.Value = 0;
        ShiftHoursBox.Value = 0;
        ShiftMinutesBox.Value = 0;
        ShiftSecondsBox.Value = 0;
    }

    private void UpdateShiftHeader()
    {
        if (ViewModel is null) return;
        int n = ViewModel.SelectionCount;
        ShiftHeaderText.Text = n > 1
            ? $"Shift the capture date of {n} images by:"
            : "Shift the capture date by:";
    }

    private void UpdateShiftPreview()
    {
        if (ViewModel is null || ShiftPreviewText is null || ApplyShiftButton is null) return;

        var offset = BuildShiftOffset();
        if (offset == TimeSpan.Zero)
        {
            ShiftPreviewText.Text = "Enter a non-zero shift (negative values shift earlier).";
            ApplyShiftButton.IsEnabled = false;
            return;
        }

        ApplyShiftButton.IsEnabled = true;
        string sign = offset < TimeSpan.Zero ? "−" : "+";
        string magnitude = FormatOffset(offset.Duration());

        if (ViewModel.CommonDateTaken.HasValue && !ViewModel.IsDateMixed)
        {
            var newDate = ViewModel.CommonDateTaken.Value + offset;
            ShiftPreviewText.Text =
                $"{sign}{magnitude}  →  {newDate.LocalDateTime:M/d/yyyy h:mm:ss tt}";
        }
        else
        {
            ShiftPreviewText.Text = $"{sign}{magnitude} (each date shifts independently)";
        }
    }

    private static string FormatOffset(TimeSpan d)
    {
        var parts = new List<string>();
        if (d.Days > 0) parts.Add($"{d.Days}d");
        if (d.Hours > 0) parts.Add($"{d.Hours}h");
        if (d.Minutes > 0) parts.Add($"{d.Minutes}m");
        if (d.Seconds > 0) parts.Add($"{d.Seconds}s");
        return parts.Count > 0 ? string.Join(" ", parts) : "0";
    }

    // Keep DetailGrid MinHeight in sync with ScrollViewer viewport
    // so the * spacer row pushes the info block to the bottom.
    private void OnDetailScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DetailGrid.MinHeight = e.NewSize.Height;
    }
}
