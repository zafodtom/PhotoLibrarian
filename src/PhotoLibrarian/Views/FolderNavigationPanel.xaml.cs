using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using PhotoLibrarian.ViewModels;
using PhotoLibrarian.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PhotoLibrarian.Views;

public sealed partial class FolderNavigationPanel : UserControl
{
    private FolderNavigationViewModel? ViewModel => App.ViewModel?.FolderNav;
    private readonly SemaphoreSlim _tagRefreshGate = new(1, 1);
    private bool _isRefreshingTagTree;
    private bool _requireAllTags = true;
    private ImageGridViewModel? _subscribedTagGrid;
    private readonly HashSet<string> _selectedTagPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedTagPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedFolderPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private const string LibraryRootSelectionKey = "__photo_library_root__";

    public FolderNavigationPanel()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    public async Task RefreshAllTreesAsync()
    {
        RefreshLibraryTree();
        await RefreshTagsTreeAsync();
    }

    public Task RefreshMetadataTreesAsync() => RefreshTagsTreeAsync();

    // Compatibility endpoints for MainWindow callers. The date, person and
    // flag trees have been removed, but album data remains untouched.
    public Task RefreshPeopleTreeAsync() => Task.CompletedTask;
    public void RefreshFlagTree() { }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (_subscribedTagGrid is null && App.ViewModel?.ImageGrid is { } grid)
        {
            _subscribedTagGrid = grid;
            grid.ResultsChanged += OnTagFacetResultsChanged;
        }
        ViewModel.RootFolders.CollectionChanged += (s, args) => RefreshLibraryTree();
        RefreshLibraryTree();
        
        // Initial metadata trees load (don't bind to CollectionChanged to avoid recursion)
        _ = RefreshTagsTreeAsync();
    }

    private void RefreshLibraryTree()
    {
        if (ViewModel is null) return;

        var expandedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFolderExpansionState(LibraryTree.RootNodes, expandedPaths);

        _selectedFolderPaths.RemoveWhere(path =>
            !string.Equals(
                path,
                LibraryRootSelectionKey,
                StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(path));

        LibraryTree.RootNodes.Clear();

        var photoLibraryRoot = new TreeViewNode
        {
            Content = new FolderNodeWrapper(
                null,
                "📚 Photo Library",
                _selectedFolderPaths.Contains(LibraryRootSelectionKey)),
            IsExpanded = true
        };

        foreach (var rootFolder in ViewModel.RootFolders)
        {
            var rootNode = BuildFolderNode(
                rootFolder,
                isRootFolder: true,
                _selectedFolderPaths);
            photoLibraryRoot.Children.Add(rootNode);
        }

        LibraryTree.RootNodes.Add(photoLibraryRoot);
        RestoreFolderExpansionState(LibraryTree.RootNodes, expandedPaths);
    }

    private static void CollectFolderExpansionState(
        IList<TreeViewNode> nodes,
        ISet<string> expandedPaths)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded &&
                node.Content is FolderNodeWrapper { FolderNode: not null } wrapper)
            {
                expandedPaths.Add(wrapper.FolderNode.Path);
            }

            if (node.Children.Count > 0)
                CollectFolderExpansionState(node.Children, expandedPaths);
        }
    }

    private static void RestoreFolderExpansionState(
        IList<TreeViewNode> nodes,
        ISet<string> expandedPaths)
    {
        foreach (var node in nodes)
        {
            if (node.Content is FolderNodeWrapper { FolderNode: not null } wrapper &&
                expandedPaths.Contains(wrapper.FolderNode.Path))
            {
                node.IsExpanded = true;
            }

            if (node.Children.Count > 0)
                RestoreFolderExpansionState(node.Children, expandedPaths);
        }
    }

    private IReadOnlyCollection<string>? GetSelectedFolderScope()
    {
        if (_selectedFolderPaths.Contains(LibraryRootSelectionKey))
            return null;

        var folders = _selectedFolderPaths
            .Where(path =>
                !string.Equals(
                    path,
                    LibraryRootSelectionKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        return folders.Count == 0 ? null : folders;
    }

    public string? GetPreferredFileOperationDirectory()
    {
        var selectedFolders = _selectedFolderPaths
            .Where(path =>
                !string.Equals(
                    path,
                    LibraryRootSelectionKey,
                    StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedFolders.Count == 1)
            return selectedFolders[0];

        return App.HasActiveAlbum &&
               !string.IsNullOrWhiteSpace(App.CurrentAlbumPath)
            ? App.CurrentAlbumPath
            : ViewModel?.RootFolders.FirstOrDefault()?.Path;
    }

    public async Task RefreshTagsTreeAsync()
    {
        if (App.ViewModel?.TagNav is null) return;

        await _tagRefreshGate.WaitAsync();
        try
        {
            await App.ViewModel.TagNav.LoadTagsAsync(GetSelectedFolderScope());

            var validTagPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var root in App.ViewModel.TagNav.RootTags)
                CollectTagPaths(root, validTagPaths);
            _selectedTagPaths.RemoveWhere(path => !validTagPaths.Contains(path));
            _excludedTagPaths.RemoveWhere(path => !validTagPaths.Contains(path));

            var completion =
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                _isRefreshingTagTree = true;
                try
                {
                    var expandedPaths = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    CollectTagTreeState(
                        TagsTree.RootNodes,
                        expandedPaths);

                    TagsTree.RootNodes.Clear();
                    foreach (var tagNode in App.ViewModel.TagNav.RootTags)
                    {
                        var node = BuildTagNode(
                            tagNode, _selectedTagPaths, _excludedTagPaths,
                            TagSearchBox.Text);
                        if (node is not null)
                            TagsTree.RootNodes.Add(node);
                    }
                    UpdateTagFacetCounts();

                    RestoreTagTreeState(
                        TagsTree.RootNodes,
                        expandedPaths);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
                finally
                {
                    _isRefreshingTagTree = false;
                }
            }))
            {
                throw new InvalidOperationException(
                    "The tag tree could not be refreshed.");
            }

            await completion.Task;
        }
        finally
        {
            _tagRefreshGate.Release();
        }
    }

    private static void CollectTagPaths(
        TagNode node,
        ISet<string> paths)
    {
        paths.Add(node.FullPath);
        foreach (var child in node.Children)
            CollectTagPaths(child, paths);
    }

    private void CollectTagTreeState(
        IList<TreeViewNode> nodes,
        HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.Content is TagNodeWrapper wrapper &&
                node.IsExpanded)
            {
                expanded.Add(wrapper.TagNode.FullPath);
            }

            if (node.Children.Count > 0)
                CollectTagTreeState(node.Children, expanded);
        }
    }

    private void RestoreTagTreeState(
        IList<TreeViewNode> nodes,
        HashSet<string> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.Content is TagNodeWrapper wrapper &&
                expanded.Contains(wrapper.TagNode.FullPath))
            {
                node.IsExpanded = true;
            }

            if (node.Children.Count > 0)
                RestoreTagTreeState(node.Children, expanded);
        }
    }

    private static TreeViewNode? BuildTagNode(
        TagNode tagNode,
        ISet<string> selectedPaths,
        ISet<string> excludedPaths,
        string? search = null,
        bool parentMatched = false)
    {
        bool searching = !string.IsNullOrWhiteSpace(search);
        bool selfMatches = !searching || parentMatched ||
            (!tagNode.IsRoot &&
             (tagNode.Name.Contains(search!, StringComparison.OrdinalIgnoreCase) ||
              tagNode.FullPath.Contains(search!, StringComparison.OrdinalIgnoreCase)));
        var treeNode = new TreeViewNode
        {
            Content = new TagNodeWrapper(
                tagNode,
                selectedPaths.Contains(tagNode.FullPath) ? 1 :
                    excludedPaths.Contains(tagNode.FullPath) ? -1 : 0),
            HasUnrealizedChildren = false,
            IsExpanded = searching || tagNode.IsRoot
        };
        foreach (var child in tagNode.Children)
        {
            var childNode = BuildTagNode(child, selectedPaths, excludedPaths,
                search, selfMatches && !tagNode.IsRoot);
            if (childNode is not null)
                treeNode.Children.Add(childNode);
        }
        // Search is a presentation-only view: no tags or filters are deleted.
        if (searching && !selfMatches && treeNode.Children.Count == 0 && !tagNode.IsRoot)
            return null;
        return treeNode;
    }

    private void OnIncludeTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TagNodeWrapper wrapper })
            ApplyTagNavigationSelection(wrapper, include: true);
    }

    private void OnExcludeTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TagNodeWrapper wrapper })
            ApplyTagNavigationSelection(wrapper, include: false);
    }

    private void OnTagCombinationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TagCombinationMode is null || TagsTree is null) return;
        _requireAllTags = TagCombinationMode.SelectedIndex != 1;
        UpdateGridFromSelection();
    }

    private void OnClearTagFiltersClick(object sender, RoutedEventArgs e)
    {
        _selectedTagPaths.Clear();
        _excludedTagPaths.Clear();
        foreach (var node in TagsTree.RootNodes)
            ClearTagNodeSelection(node);
        UpdateTagSummary();
        UpdateGridFromSelection();
    }

    private static void ClearTagNodeSelection(TreeViewNode node)
    {
        if (node.Content is TagNodeWrapper wrapper)
            wrapper.FilterState = 0;
        foreach (var child in node.Children)
            ClearTagNodeSelection(child);
    }

    private void OnTagSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (App.ViewModel?.TagNav is null) return;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTagTreeState(TagsTree.RootNodes, expanded);
        TagsTree.RootNodes.Clear();
        foreach (var tag in App.ViewModel.TagNav.RootTags)
        {
            var node = BuildTagNode(tag, _selectedTagPaths, _excludedTagPaths,
                TagSearchBox.Text);
            if (node is not null) TagsTree.RootNodes.Add(node);
        }
        if (string.IsNullOrWhiteSpace(TagSearchBox.Text))
            RestoreTagTreeState(TagsTree.RootNodes, expanded);
        UpdateTagFacetCounts();
    }

    private void OnTagFacetResultsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateTagFacetCounts);
    }

    private void UpdateTagFacetCounts()
    {
        var grid = App.ViewModel?.ImageGrid;
        if (grid is null) return;
        foreach (var node in TagsTree.RootNodes)
            UpdateTagNodeFacetCount(node, grid.TagFacetCounts);
        TagMatchesText.Text = $"{grid.Images.Count:N0} matched photos";
        UpdateTagSummary();
    }

    private static void UpdateTagNodeFacetCount(
        TreeViewNode node, IReadOnlyDictionary<string, int> counts)
    {
        if (node.Content is TagNodeWrapper wrapper)
            wrapper.ResultCount = counts.TryGetValue(wrapper.TagNode.FullPath, out var count)
                ? count : 0;
        foreach (var child in node.Children)
            UpdateTagNodeFacetCount(child, counts);
    }

    private void UpdateTagSummary()
    {
        if (ActiveTagFiltersText is null) return;
        var positive = _selectedTagPaths.OrderBy(x => x).Select(x =>
            "+ " + (x == "" ? "Tags" : x == "__untagged__" ? "Untagged" : x));
        var negative = _excludedTagPaths.OrderBy(x => x).Select(x =>
            "− " + (x == "" ? "Tags" : x == "__untagged__" ? "Untagged" : x));
        var active = positive.Concat(negative).ToArray();
        ActiveTagFiltersText.Text = active.Length == 0
            ? "No tag conditions"
            : string.Join("  |  ", active);
    }

    private void OnTagRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TagNodeWrapper wrapper })
            return;
        ApplyTagNavigationSelection(wrapper, include: true);
        e.Handled = true;
    }

    private void OnTagRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TagNodeWrapper wrapper })
            return;
        ApplyTagNavigationSelection(wrapper, include: false);
        e.Handled = true;
    }

    private void ApplyTagNavigationSelection(TagNodeWrapper wrapper, bool include)
    {
        if (_isRefreshingTagTree) return;

        var path = wrapper.TagNode.FullPath;
        var desired = include ? 1 : -1;
        // Clicking an already-selected mode toggles it off.
        var next = wrapper.FilterState == desired ? 0 : desired;
        _selectedTagPaths.Remove(path);
        _excludedTagPaths.Remove(path);
        if (next == 1)
            _selectedTagPaths.Add(path);
        else if (next == -1)
            _excludedTagPaths.Add(path);
        wrapper.FilterState = next;
        UpdateTagSummary();
        UpdateGridFromSelection();
    }

    private async void OnManageTagCatalogClick(object sender, RoutedEventArgs e)
    {
        if (App.ViewModel?.MetadataPanel is not MetadataPanelViewModel metadata)
            return;

        await metadata.ReloadAvailableTagsAsync();

        var primaryButtonStyle =
            (Style)Application.Current.Resources["HiTechPrimaryButtonStyle"];
        var iconButtonStyle =
            (Style)Application.Current.Resources["HiTechIconButtonStyle"];
        var inputStyle =
            (Style)Application.Current.Resources["HiTechInputStyle"];
        var comboStyle =
            (Style)Application.Current.Resources["HiTechComboStyle"];

        var accentBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 83, 215, 232));
        var borderBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 36, 66, 82));
        var strongBorderBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 52, 115, 138));
        var panelBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 10, 18, 26));
        var secondaryTextBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 140, 169, 179));

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            MinWidth = 0,
            MinHeight = 118,
            MaxHeight = 360,
            CanDragItems = true,
            CanReorderItems = false,
            AllowDrop = true,
            Background = panelBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };

        var addTagButton = new Button
        {
            Content = "NEW TAG",
            Style = primaryButtonStyle
        };
        var editButton = new Button
        {
            Content = "EDIT",
            Style = iconButtonStyle,
            IsEnabled = false
        };
        var removeButton = new Button
        {
            Content = "REMOVE",
            Style = iconButtonStyle,
            IsEnabled = false
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        toolbar.Children.Add(addTagButton);
        toolbar.Children.Add(editButton);
        toolbar.Children.Add(removeButton);

        var hint = new TextBlock
        {
            Text = "Drag a tag onto another to nest it, or drop it on ROOT to move it to the top level. Removing a used tag keeps its photo assignments.",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = secondaryTextBrush
        };

        var managerStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Width = 460
        };

        var consoleHeader = new Grid
        {
            Height = 46
        };
        consoleHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(6) });
        consoleHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        consoleHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var headerRail = new Rectangle
        {
            Width = 4,
            Fill = accentBrush
        };
        consoleHeader.Children.Add(headerRail);

        var headerText = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1
        };
        Grid.SetColumn(headerText, 1);
        headerText.Children.Add(new TextBlock
        {
            Text = "TAG // CATALOG",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 100,
            Foreground = new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 230, 247, 250))
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "HIERARCHY / ASSIGNMENT / INDEX",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            CharacterSpacing = 150,
            Foreground = strongBorderBrush
        });
        consoleHeader.Children.Add(headerText);

        var headerCode = new TextBlock
        {
            Text = "CAT // T01",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            CharacterSpacing = 120,
            Foreground = strongBorderBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerCode, 2);
        consoleHeader.Children.Add(headerCode);

        var rootDropZone = new Border
        {
            MinHeight = 38,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(1),
            BorderThickness = new Thickness(1),
            BorderBrush = strongBorderBrush,
            Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 13, 29, 37)),
            AllowDrop = true,
            Child = new TextBlock
            {
                Text = "ROOT LEVEL  //  DROP TAG HERE",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                CharacterSpacing = 70,
                Foreground = accentBrush
            }
        };

        content.Children.Add(consoleHeader);
        content.Children.Add(toolbar);
        content.Children.Add(hint);
        content.Children.Add(managerStatus);
        var emptyCatalogHint = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        emptyCatalogHint.Children.Add(new TextBlock
        {
            Text = "CATALOG EMPTY",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = accentBrush
        });
        emptyCatalogHint.Children.Add(new TextBlock
        {
            Text = "USE NEW TAG TO INITIALIZE THE CATALOG",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = secondaryTextBrush
        });
        var listHost = new Grid { MinHeight = 230 };
        listHost.Children.Add(list);
        listHost.Children.Add(emptyCatalogHint);
        content.Children.Add(listHost);
        content.Children.Add(rootDropZone);

        var dialogHost = new Border
        {
            Background = panelBrush,
            BorderBrush = strongBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = content
        };

        var dialog = new ContentDialog
        {
            Title = null,
            Content = dialogHost,
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Background = panelBrush,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 247, 250)),
            BorderBrush = strongBorderBrush,
            BorderThickness = new Thickness(1)
        };

        var closeConsole = new Button
        {
            Content = "CLOSE CONSOLE",
            Width = 152,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = iconButtonStyle
        };
        closeConsole.Click += (_, _) => dialog.Hide();
        var footerRail = new Border
        {
            BorderBrush = strongBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = closeConsole
        };
        content.Children.Add(footerRail);

        void ShowManagerStatus(string? message)
        {
            managerStatus.Text = message ?? "";
            managerStatus.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        void ReloadRows()
        {
            var rows = metadata.AvailableTags
                .Select(item => new CatalogManagerRow(item))
                .ToList();
            list.ItemsSource = rows;
            emptyCatalogHint.Visibility = rows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            editButton.IsEnabled = false;
            removeButton.IsEnabled = false;
            ShowManagerStatus(null);
        }

        CatalogManagerRow? draggedRow = null;

        async Task MoveCatalogItemAsync(
            CatalogManagerRow moving,
            string? destinationParent)
        {
            var newFullPath = string.IsNullOrWhiteSpace(destinationParent)
                ? moving.Item.Name
                : $"{destinationParent}/{moving.Item.Name}";

            if (string.Equals(
                newFullPath,
                moving.Item.Tag,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var validation = metadata.ValidateCatalogPath(
                newFullPath,
                moving.Item.Tag);
            if (validation is not null)
            {
                ShowManagerStatus(validation);
                return;
            }

            try
            {
                await metadata.RenameCatalogItemAsync(
                    moving.Item,
                    newFullPath);
                await metadata.ReloadAvailableTagsAsync();
                await RefreshTagsTreeAsync();
                ReloadRows();
            }
            catch (Exception ex)
            {
                ShowManagerStatus($"Move failed: {ex.Message}");
            }
        }

        void OnCatalogRowDragOver(object sender, DragEventArgs args)
        {
            if (draggedRow is null ||
                sender is not ListViewItem { Content: CatalogManagerRow target })
            {
                args.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            if (string.Equals(
                    target.Item.Tag,
                    draggedRow.Item.Tag,
                    StringComparison.OrdinalIgnoreCase) ||
                target.Item.Tag.StartsWith(
                    draggedRow.Item.Tag + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                args.AcceptedOperation = DataPackageOperation.None;
                args.Handled = true;
                return;
            }

            args.AcceptedOperation = DataPackageOperation.Move;
            args.DragUIOverride.Caption = $"Move into '{target.Item.Tag}'";
            args.Handled = true;
        }

        async void OnCatalogRowDrop(object sender, DragEventArgs args)
        {
            args.Handled = true;

            if (draggedRow is null ||
                sender is not ListViewItem { Content: CatalogManagerRow target })
            {
                return;
            }

            var moving = draggedRow;
            draggedRow = null;
            await MoveCatalogItemAsync(moving, target.Item.Tag);
        }

        void OnCatalogRootDragOver(object sender, DragEventArgs args)
        {
            if (draggedRow is null)
            {
                args.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            args.AcceptedOperation = DataPackageOperation.Move;
            args.DragUIOverride.Caption = "Move to root";
            args.Handled = true;
        }

        async void OnCatalogRootDrop(object sender, DragEventArgs args)
        {
            args.Handled = true;

            if (draggedRow is null)
                return;

            var moving = draggedRow;
            draggedRow = null;
            await MoveCatalogItemAsync(moving, null);
        }

        list.SelectionChanged += (_, _) =>
        {
            editButton.IsEnabled = list.SelectedItems.Count == 1;
            removeButton.IsEnabled = list.SelectedItems.Count > 0;
        };

        addTagButton.Click += async (_, _) =>
        {
            var parentTags = metadata.AvailableTags
                .Select(item => item.Tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parentBox = new ComboBox
            {
                Header = "Parent tag",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = comboStyle
            };
            parentBox.Items.Add("(root)");
            foreach (var parentTag in parentTags)
                parentBox.Items.Add(parentTag);
            parentBox.SelectedIndex = 0;

            var nameBox = new TextBox
            {
                Header = "Tag name",
                PlaceholderText = "e.g. Red",
                Style = inputStyle
            };

            var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = "TAG // CREATE",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                CharacterSpacing = 100,
                Foreground = accentBrush
            });
            panel.Children.Add(new Border { Height = 1, Background = borderBrush });
            panel.Children.Add(parentBox);
            panel.Children.Add(nameBox);

            var createDialog = new ContentDialog
            {
                Title = "TAG CREATION",
                Content = new Border
                {
                    Background = panelBrush,
                    BorderBrush = strongBorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14),
                    Child = panel
                },
                PrimaryButtonText = "CREATE TAG",
                CloseButtonText = "CANCEL",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            dialog.Hide();
            var createTagResult = await createDialog.ShowAsync();
            _ = dialog.ShowAsync();
            if (createTagResult != ContentDialogResult.Primary)
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

            var validation = metadata.ValidateCatalogPath(fullTag);
            if (validation is not null)
            {
                ShowManagerStatus(validation);
                return;
            }

            AlbumService.AddSelectedTag(fullTag);
            await metadata.ReloadAvailableTagsAsync();
            await RefreshTagsTreeAsync();
            ReloadRows();
        };

        editButton.Click += async (_, _) =>
        {
            if (list.SelectedItem is not CatalogManagerRow row)
                return;

            var item = row.Item;
            var parentTags = metadata.AvailableTags
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
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = comboStyle
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
                Text = item.Name,
                Style = inputStyle
            };

            var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = "TAG // MODIFY",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                CharacterSpacing = 100,
                Foreground = accentBrush
            });
            panel.Children.Add(new Border { Height = 1, Background = borderBrush });
            panel.Children.Add(parentBox);
            panel.Children.Add(nameBox);

            var editDialog = new ContentDialog
            {
                Title = "TAG MODIFICATION",
                Content = new Border
                {
                    Background = panelBrush,
                    BorderBrush = strongBorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14),
                    Child = panel
                },
                PrimaryButtonText = "SAVE TAG",
                CloseButtonText = "CANCEL",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ElementTheme.Dark
            };

            dialog.Hide();
            var editResult = await editDialog.ShowAsync();
            _ = dialog.ShowAsync();
            if (editResult != ContentDialogResult.Primary)
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

            var validation = metadata.ValidateCatalogPath(newFullPath, item.Tag);
            if (validation is not null)
            {
                ShowManagerStatus(validation);
                return;
            }

            try
            {
                await metadata.RenameCatalogItemAsync(item, newFullPath);
                await metadata.ReloadAvailableTagsAsync();
                await RefreshTagsTreeAsync();
                ReloadRows();
            }
            catch (Exception ex)
            {
                App.ViewModel.StatusText = $"Rename failed: {ex.Message}";
            }
        };

        removeButton.Click += async (_, _) =>
        {
            var selected = list.SelectedItems.OfType<CatalogManagerRow>()
                .Select(row => row.Item).ToList();
            if (selected.Count == 0)
                return;

            // Keep the catalog modal visible: a confirmation rail is shown
            // inside it rather than opening a competing native ContentDialog.
            var warning = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 10, 17)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 74, 92)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(12)
            };
            var warningBody = new StackPanel { Spacing = 8 };
            warningBody.Children.Add(new TextBlock
            {
                Text = "WARNING // TAG CATALOG REMOVAL",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 92, 105))
            });
            warningBody.Children.Add(new TextBlock
            {
                Text = $"Remove {selected.Count} catalog tag(s)? Existing photo assignments are kept.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondaryTextBrush
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var proceed = new Button { Content = "CONFIRM REMOVE", Style = iconButtonStyle };
            var abort = new Button { Content = "ABORT", Style = iconButtonStyle };
            actions.Children.Add(proceed);
            actions.Children.Add(abort);
            warningBody.Children.Add(actions);
            warning.Child = warningBody;
            var index = content.Children.IndexOf(footerRail);
            content.Children.Insert(index, warning);
            removeButton.IsEnabled = false;
            editButton.IsEnabled = false;
            abort.Click += (_, _) =>
            {
                content.Children.Remove(warning);
                removeButton.IsEnabled = list.SelectedItems.Count > 0;
                editButton.IsEnabled = list.SelectedItems.Count == 1;
            };
            proceed.Click += async (_, _) =>
            {
                proceed.IsEnabled = false;
                abort.IsEnabled = false;
                try
                {
                    foreach (var item in selected)
                        await metadata.RemoveCatalogItemAsync(item);
                    await metadata.ReloadAvailableTagsAsync();
                    await RefreshTagsTreeAsync();
                    ReloadRows();
                }
                catch (Exception ex)
                {
                    ShowManagerStatus($"Tag removal failed: {ex.Message}");
                }
                finally
                {
                    content.Children.Remove(warning);
                    removeButton.IsEnabled = list.SelectedItems.Count > 0;
                    editButton.IsEnabled = list.SelectedItems.Count == 1;
                }
            };
        };

        var configuredContainers = new HashSet<ListViewItem>();

        list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.ItemContainer is not ListViewItem container)
                return;

            if (!configuredContainers.Add(container))
                return;

            container.CanDrag = true;
            container.AllowDrop = true;
            container.DragStarting += (_, _) =>
            {
                if (container.Content is CatalogManagerRow row)
                {
                    draggedRow = row;
                    ShowManagerStatus(null);
                }
            };
            container.DragOver += OnCatalogRowDragOver;
            container.Drop += OnCatalogRowDrop;
        };

        list.DragItemsStarting += (_, args) =>
        {
            draggedRow = args.Items.OfType<CatalogManagerRow>().FirstOrDefault();
            ShowManagerStatus(null);
        };

        list.DragItemsCompleted += (_, _) =>
        {
            draggedRow = null;
        };

        rootDropZone.DragOver += OnCatalogRootDragOver;
        rootDropZone.Drop += OnCatalogRootDrop;

        ReloadRows();
        await dialog.ShowAsync();
    }

    private void OnExpandAllTagsClick(object sender, RoutedEventArgs e)
    {
        SetTagTreeExpansion(TagsTree.RootNodes, true);
    }

    private void OnCollapseAllTagsClick(object sender, RoutedEventArgs e)
    {
        // Collapse every node including the synthetic Tags root.
        SetTagTreeExpansion(TagsTree.RootNodes, false);
    }

    private static void SetTagTreeExpansion(
        IList<TreeViewNode> nodes,
        bool expanded)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
            {
                node.IsExpanded = expanded;
                SetTagTreeExpansion(node.Children, expanded);
            }
        }
    }

    private static TreeViewNode BuildFolderNode(
        FolderNode folderNode,
        bool isRootFolder = false,
        ISet<string>? selectedPaths = null)
    {
        // Create display text with icon
        string icon = "";
        string displayName = folderNode.Name;
        string displayText = isRootFolder 
            ? $"{icon} {folderNode.Path}"  // Show full path for root folders
            : $"{icon} {displayName}";     // Show just name for subfolders

        // Store FolderNode in a wrapper for event handlers to retrieve
        var treeNode = new TreeViewNode 
        { 
            Content = new FolderNodeWrapper(
                folderNode,
                displayText,
                selectedPaths?.Contains(folderNode.Path) == true),
            IsExpanded = false
        };

        // Check if this node has placeholder children (lazy-load marker)
        bool hasPlaceholder = folderNode.Children.Count == 1 && folderNode.Children[0].Path == "";

        if (hasPlaceholder)
        {
            treeNode.HasUnrealizedChildren = true;
        }
        else if (folderNode.Children.Count > 0)
        {
            // Add realized children
            foreach (var child in folderNode.Children)
            {
                treeNode.Children.Add(BuildFolderNode(
                    child,
                    isRootFolder: false,
                    selectedPaths));
            }
        }

        return treeNode;
    }

    private void OnLibraryTreeRightTapped(
        object sender,
        RightTappedRoutedEventArgs e)
    {
        var node = FindLibraryNodeAt(e);
        string? targetPath = null;
        var isAlbumRoot = false;

        if (node?.Content is FolderNodeWrapper wrapper &&
            wrapper.FolderNode is not null)
        {
            targetPath = wrapper.FolderNode.Path;
        }
        else if (node?.Content is FolderNodeWrapper
                 {
                     FolderNode: null
                 } &&
                 App.HasActiveAlbum)
        {
            targetPath = App.CurrentAlbumPath;
            isAlbumRoot = true;
        }

        if (string.IsNullOrWhiteSpace(targetPath) ||
            !Directory.Exists(targetPath))
        {
            return;
        }

        var menu = new MenuFlyout();

        var newFolder = new MenuFlyoutItem { Text = "New Folder" };
        newFolder.Click += async (_, _) =>
        {
            try
            {
                Services.PhotoOperationsService.CreateNewFolder(targetPath);
                await App.ViewModel.RefreshFolderStructureAsync();
            }
            catch (Exception ex)
            {
                App.ViewModel.StatusText = $"Create folder failed: {ex.Message}";
            }
        };
        menu.Items.Add(newFolder);

        var paste = new MenuFlyoutItem { Text = "Paste" };
        paste.Click += async (_, _) =>
        {
            try
            {
                var pasted = await App.ViewModel.PhotoOps
                    .PasteClipboardToDirectoryAsync(targetPath);
                if (pasted.Count > 0)
                    await App.ViewModel.RefreshKnownFilesystemChangesAsync(
                        pasted,
                        refreshFolderTree: true);
            }
            catch (Exception ex)
            {
                App.ViewModel.StatusText = $"Paste failed: {ex.Message}";
            }
        };
        menu.Items.Add(paste);

        // Destructive catalog removal belongs exclusively to the active
        // Photo Library root, not individual photo folders.
        if (isAlbumRoot &&
            string.Equals(
                System.IO.Path.GetFullPath(targetPath).TrimEnd(
                    System.IO.Path.DirectorySeparatorChar),
                System.IO.Path.GetFullPath(App.CurrentAlbumPath!).TrimEnd(
                    System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var deleteAlbum = new MenuFlyoutItem { Text = "DELETE ALBUM  //  WARNING" };
            deleteAlbum.Click += async (_, _) =>
            {
                if (App.MainWindow is MainWindow mainWindow)
                    await mainWindow.ConfirmDeleteAlbumAsync();
            };
            menu.Items.Add(deleteAlbum);
        }

        if (!isAlbumRoot)
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            var copy = new MenuFlyoutItem { Text = "Copy" };
            copy.Click += async (_, _) =>
            {
                try
                {
                    await Services.PhotoOperationsService
                        .CopyPathsToClipboardAsync([targetPath], cut: false);
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"[CLIPBOARD] Folder copy failed: {ex}");
                    if (App.ViewModel is { } main)
                        main.StatusText = $"Could not copy folder: {ex.Message}";
                }
            };
            menu.Items.Add(copy);

            var cut = new MenuFlyoutItem { Text = "Cut" };
            cut.Click += async (_, _) =>
            {
                try
                {
                    await Services.PhotoOperationsService
                        .CopyPathsToClipboardAsync([targetPath], cut: true);
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"[CLIPBOARD] Folder cut failed: {ex}");
                    if (App.ViewModel is { } main)
                        main.StatusText = $"Could not cut folder: {ex.Message}";
                }
            };
            menu.Items.Add(cut);

            var rename = new MenuFlyoutItem { Text = "Rename…" };
            rename.Click += async (_, _) =>
                await ShowRenameFolderDialogAsync(targetPath);
            menu.Items.Add(rename);

            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += async (_, _) =>
            {
                await App.ViewModel.DeleteDirectoryAsync(targetPath);
            };
            menu.Items.Add(delete);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        var open = new MenuFlyoutItem { Text = "Open in File Explorer" };
        open.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{targetPath}\"",
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                App.ViewModel.StatusText = $"File Explorer failed to open: {ex.Message}";
            }
        };
        menu.Items.Add(open);

        menu.ShowAt(
            LibraryTree,
            e.GetPosition(LibraryTree));
        e.Handled = true;
    }

    private TreeViewNode? FindLibraryNodeAt(RightTappedRoutedEventArgs e)
    {
        try
        {
            var elements = VisualTreeHelper.FindElementsInHostCoordinates(
                e.GetPosition(null),
                LibraryTree);

            foreach (var element in elements)
            {
                if (element is not TreeViewItem item)
                    continue;

                var node = LibraryTree.NodeFromContainer(item);
                if (node is not null)
                    return node;

                if (item.DataContext is TreeViewNode dataNode)
                    return dataNode;
            }
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"Library context hit-test failed: {ex.Message}");
        }

        return null;
    }

    private async Task ShowRenameFolderDialogAsync(string folderPath)
    {
        var currentName = Path.GetFileName(folderPath);
        var box = new TextBox
        {
            Text = currentName,
            SelectionStart = 0,
            SelectionLength = currentName.Length
        };

        var dialog = new ContentDialog
        {
            Title = "Rename folder",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var newName = box.Text?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        var newPath = await App.ViewModel.PhotoOps
            .RenameDirectoryAsync(folderPath, newName);
        if (newPath is null)
        {
            App.ViewModel.StatusText = "Folder rename failed.";
            return;
        }

        await App.ViewModel.RefreshKnownLibraryStateAsync(
            refreshFolderTree: true);
    }

    private async void OnManageFoldersClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not Window owner) return;

        try
        {
            var albumPath = await AlbumService.PickAlbumFolderAsync(owner);
            if (albumPath is not null)
                AlbumService.RestartForAlbum(albumPath);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Could not open album",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.RefreshCommand.CanExecute(null) == true)
            await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnFolderFilterCheckBoxClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not CheckBox
            {
                Tag: FolderNodeWrapper wrapper
            } checkBox)
        {
            return;
        }

        var isChecked = checkBox.IsChecked == true;
        wrapper.IsSelected = isChecked;

        var key = wrapper.FolderNode?.Path ?? LibraryRootSelectionKey;
        if (isChecked)
            _selectedFolderPaths.Add(key);
        else
            _selectedFolderPaths.Remove(key);

        await RefreshTagsTreeAsync();
        UpdateGridFromSelection();
    }

    private void OnLibraryExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.Content is not FolderNodeWrapper wrapper || wrapper.FolderNode is null) return;

        var folderNode = wrapper.FolderNode;

        // Check if we need to load placeholder children
        bool hasPlaceholder = folderNode.Children.Count == 1 && folderNode.Children[0].Path == "";
        
        if (hasPlaceholder)
        {
            // Clear placeholder and load real children
            folderNode.Children.Clear();
            FolderNavigationViewModel.BuildChildNodes(folderNode);

            // Rebuild the TreeViewNode children
            args.Node.Children.Clear();
            args.Node.HasUnrealizedChildren = false;

            foreach (var child in folderNode.Children)
            {
                args.Node.Children.Add(BuildFolderNode(
                    child,
                    isRootFolder: false,
                    _selectedFolderPaths));
            }
        }
    }

    private void UpdateGridFromSelection()
    {
        if (App.ViewModel?.ImageGrid is null) return;

        // Folder filtering uses independent checkbox state, just like tags.
        bool photoLibraryRootSelected =
            _selectedFolderPaths.Contains(LibraryRootSelectionKey);
        var selectedFolders = _selectedFolderPaths
            .Where(path =>
                !string.Equals(
                    path,
                    LibraryRootSelectionKey,
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Tag filtering uses an independent selection set rather than
        // TreeView's hierarchical multiple-selection semantics.
        bool tagRootSelected = _selectedTagPaths.Contains("");
        bool untaggedSelected = _selectedTagPaths.Contains("__untagged__");
        var selectedTags = _selectedTagPaths
            .Where(path =>
                !string.IsNullOrEmpty(path) &&
                !string.Equals(
                    path,
                    "__untagged__",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var excludedTags = _excludedTagPaths.ToList();

        // If no folder and no tag conditions are selected, show an empty grid.
        if (!photoLibraryRootSelected && selectedFolders.Count == 0 &&
            !tagRootSelected && !untaggedSelected && selectedTags.Count == 0 &&
            excludedTags.Count == 0)
        {
            App.ViewModel.ImageGrid.ClearFilterCommand.Execute(null);
            return;
        }

        // If Photo Library root is selected and nothing else, treat as "show all from all folders"
        if (photoLibraryRootSelected && selectedFolders.Count == 0)
        {
            // Add all root folder paths
            selectedFolders.AddRange(ViewModel?.RootFolders.Select(f => f.Path) ?? []);
        }

        _ = App.ViewModel.ImageGrid.FilterByMultipleCriteriaAsync(
            selectedFolders.Count > 0 ? selectedFolders : null,
            dateRootSelected: false, years: null, months: null,
            peopleRootSelected: false, personIds: null,
            tagRootSelected: tagRootSelected,
            tags: selectedTags.Count > 0 ? selectedTags : null,
            untaggedSelected: untaggedSelected,
            flaggedSelected: false,
            excludedTags: excludedTags.Count > 0 ? excludedTags : null,
            requireAllIncludedTags: _requireAllTags);
    }

    // ============================================================================
    //  Drag-and-drop: drop photos from the grid onto a tag node to apply that tag.
    // ============================================================================

    private void OnTagsTreeDragOver(object sender, DragEventArgs e)
    {
        var hasMarker = e.DataView.Properties.ContainsKey(Services.PhotoOperationsService.TagDropFormatId);
        if (!hasMarker)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var tagNode = FindTagNodeAt(e);
        if (tagNode == null || tagNode.IsRoot)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Apply tag '{tagNode.FullPath}'";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private async void OnTagsTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Properties.ContainsKey(Services.PhotoOperationsService.TagDropFormatId)) return;

        var tagNode = FindTagNodeAt(e);
        DebugLog.WriteLine($"OnTagsTreeDrop: tagNode={tagNode?.FullPath ?? "null"}");
        if (tagNode == null || tagNode.IsRoot) return;

        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                DebugLog.WriteLine("OnTagsTreeDrop: no StorageItems in DataView");
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(f => f.Path).ToList();
            DebugLog.WriteLine($"OnTagsTreeDrop: applying tag '{tagNode.FullPath}' to {paths.Count} path(s)");
            if (paths.Count == 0) return;

            if (App.ViewModel != null)
            {
                await App.ViewModel.ApplyTagToImagePathsAsync(tagNode.FullPath, paths);
            }
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"OnTagsTreeDrop: ERROR {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private TagNode? FindTagNodeAt(DragEventArgs e)
    {
        // WinUI 3 TreeViewItems don't have AllowDrop=true by default, so the OS drag hit-test
        // stops at the TreeView itself and e.OriginalSource is always the TreeView. We work around
        // this by hit-testing from the cursor position (in xaml-root coords) limited to TagsTree's
        // subtree, which finds the actual TreeViewItem under the pointer regardless of AllowDrop.
        try
        {
            var elements = VisualTreeHelper.FindElementsInHostCoordinates(
                e.GetPosition(null),
                TagsTree);

            foreach (var el in elements)
            {
                if (el is TreeViewItem item)
                {
                    var node = TagsTree.NodeFromContainer(item);
                    if (node?.Content is TagNodeWrapper w1) return w1.TagNode;
                    if (item.DataContext is TreeViewNode tvn && tvn.Content is TagNodeWrapper w2) return w2.TagNode;
                    if (item.DataContext is TagNodeWrapper w3) return w3.TagNode;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.WriteLine($"FindTagNodeAt: hit-test failed: {ex.Message}");
        }
        return null;
    }

    // Helper class to wrap FolderNode with display text for TreeView
    private class FolderNodeWrapper
    {
        public FolderNode? FolderNode { get; }
        public string DisplayText { get; }
        public bool IsSelected { get; set; }

        public FolderNodeWrapper(
            FolderNode? folderNode,
            string displayText,
            bool isSelected = false)
        {
            FolderNode = folderNode;
            DisplayText = displayText;
            IsSelected = isSelected;
        }

        public override string ToString() => DisplayText;
    }

    private sealed class CatalogManagerRow
    {
        public AvailableTagItem Item { get; }

        public CatalogManagerRow(AvailableTagItem item)
        {
            Item = item;
        }

        public override string ToString()
        {
            var indent = new string(' ', Math.Max(0, Item.Depth) * 4);
            var state = string.IsNullOrWhiteSpace(Item.SourceLabel)
                ? ""
                : $"  [{Item.SourceLabel}]";
            return $"{indent}{Item.Name}{state}";
        }
    }

    // Helper class to wrap TagNode for TreeView
    private class TagNodeWrapper : System.ComponentModel.INotifyPropertyChanged
    {
        public TagNode TagNode { get; }
        private int _filterState;
        private int _resultCount;
        public int ResultCount
        {
            get => _resultCount;
            set
            {
                if (_resultCount == value) return;
                _resultCount = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayText)));
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayOpacity)));
            }
        }
        public string DisplayText => $"{TagNode.Name} ({ResultCount})";
        public double DisplayOpacity => ResultCount == 0 && FilterState == 0 ? 0.46 : 1.0;
        public int FilterState
        {
            get => _filterState;
            set
            {
                if (_filterState == value) return;
                _filterState = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(FilterMarker)));
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayOpacity)));
            }
        }

        public string FilterMarker => _filterState switch
        {
            1 => "+",
            -1 => "−",
            _ => " "
        };

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public TagNodeWrapper(TagNode tagNode, int filterState = 0)
        {
            TagNode = tagNode;
            _filterState = filterState;
            _resultCount = tagNode.Count;
        }

        public override string ToString() => $"{TagNode.Name} ({TagNode.Count})";
    }
}
