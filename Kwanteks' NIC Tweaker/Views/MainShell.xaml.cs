using Kwanteks.NicTweaker.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Kwanteks.NicTweaker.Views;

public sealed partial class MainShell : UserControl
{
    public MainViewModel ViewModel { get; }

    public MainShell()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
        // ScrollViewer eats ComboBox light-dismiss; close open dropdowns on outside press / scroll.
        AddHandler(PointerPressedEvent, new PointerEventHandler(RootGrid_PointerPressed), handledEventsToo: true);
        Loaded += async (_, _) =>
        {
            MainScroll.ViewChanged += (_, _) => CloseOpenComboBoxes(this);

            if (App.MainAppWindow is MainWindow window)
                window.SetTitleBar(AppTitleBar);

            await ViewModel.InitializeAsync();
            App.SetWindowOpacity(ViewModel.OpacityReduced);
            ApplyColumnLayout(App.MainAppWindow?.Bounds.Width ?? ActualWidth);

            // Wait for first adapter load so import preview has live values.
            for (var i = 0; i < 80 && ViewModel.IsBusy; i++)
                await Task.Delay(50);

            var pending = App.TakePendingImportPath();
            if (!string.IsNullOrWhiteSpace(pending) && File.Exists(pending))
                await ViewModel.ConfigTransfer.ImportFromPathAsync(pending);
        };
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Import config";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault(f =>
            f.FileType.Equals(".nttweak", StringComparison.OrdinalIgnoreCase) ||
            f.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            ViewModel.StatusMessage = "Drop a .nttweak or .json config file.";
            ViewModel.StatusIsError = true;
            return;
        }
        await ViewModel.ConfigTransfer.ImportFromPathAsync(file.Path);
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        for (var d = src; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ComboBox { IsDropDownOpen: true })
                return;
        }

        CloseOpenComboBoxes(this);
    }

    private static void CloseOpenComboBoxes(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ComboBox { IsDropDownOpen: true } box)
                box.IsDropDownOpen = false;
            CloseOpenComboBoxes(child);
        }
    }

    private void ColumnsHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = App.MainAppWindow?.Bounds.Width ?? e.NewSize.Width;
        ApplyColumnLayout(width);
    }

    private void ApplyColumnLayout(double width)
    {
        if (width >= 1450)
        {
            SetCol(Col1, 0, 0, 1);
            SetCol(Col2, 1, 0, 1);
            SetCol(Col3, 2, 0, 1);
        }
        else if (width >= 1100)
        {
            SetCol(Col1, 0, 0, 1);
            SetCol(Col2, 1, 0, 1);
            SetCol(Col3, 0, 1, 2);
        }
        else
        {
            SetCol(Col1, 0, 0, 1);
            SetCol(Col2, 0, 1, 1);
            SetCol(Col3, 0, 2, 1);
        }
    }

    private static void SetCol(FrameworkElement el, int column, int row, int span)
    {
        Grid.SetColumn(el, column);
        Grid.SetRow(el, row);
        Grid.SetColumnSpan(el, span);
    }

    private void Opacity_Click(object sender, RoutedEventArgs e) =>
        App.SetWindowOpacity(ViewModel.OpacityReduced);

    private void ToggleLog_Click(object sender, RoutedEventArgs e) =>
        ViewModel.LogExpanded = !ViewModel.LogExpanded;

    private void DismissWarning_Click(object sender, RoutedEventArgs e) =>
        ViewModel.DismissFirstLaunchWarningCommand.Execute(null);

    private async void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var box = new ComboBox
        {
            ItemsSource = ViewModel.SectionTitles,
            SelectedIndex = 0,
            Width = 280,
            MinHeight = 26,
            FontSize = 12
        };
        var dialog = new ContentDialog
        {
            Title = "Reset section to defaults",
            Content = box,
            PrimaryButtonText = "Reset UI",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.ResetSectionToDefaults(box.SelectedItem as string);
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var snaps = await ViewModel.ListSnapshotsAsync();
        if (snaps.Count == 0)
        {
            ViewModel.StatusMessage = "No snapshots found.";
            ViewModel.StatusIsError = true;
            return;
        }

        var box = new ComboBox
        {
            Width = 420,
            ItemsSource = snaps.Select(s => $"{s.Id}|{s.CreatedUtc:yyyy-MM-dd HH:mm} — {s.Label}").ToList(),
            SelectedIndex = 0,
            MinHeight = 26,
            FontSize = 12
        };

        var dialog = new ContentDialog
        {
            Title = "Restore snapshot",
            Content = box,
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var id = (box.SelectedItem as string)?.Split('|')[0];
        if (!string.IsNullOrWhiteSpace(id))
            await ViewModel.RestoreSnapshotByIdAsync(id);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var snaps = await ViewModel.ListSnapshotsAsync();
        if (snaps.Count == 0)
        {
            ViewModel.StatusMessage = "No snapshots to export.";
            ViewModel.StatusIsError = true;
            return;
        }

        var box = new ComboBox
        {
            Width = 420,
            ItemsSource = snaps.Select(s => $"{s.Id}|{s.CreatedUtc:yyyy-MM-dd HH:mm} — {s.Label}").ToList(),
            SelectedIndex = 0,
            MinHeight = 26,
            FontSize = 12
        };
        var dialog = new ContentDialog
        {
            Title = "Export snapshot",
            Content = box,
            PrimaryButtonText = "Choose file…",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var id = (box.SelectedItem as string)?.Split('|')[0];
        if (string.IsNullOrWhiteSpace(id)) return;

        var window = App.MainAppWindow;
        if (window is null) return;
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        picker.SuggestedFileName = $"Kwanteks-NIC-Tweaker-{id}";
        picker.FileTypeChoices.Add("JSON", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await ViewModel.ExportSnapshotAsync(id, file.Path);
        ViewModel.StatusMessage = "Exported.";
        ViewModel.StatusIsError = false;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var window = App.MainAppWindow;
        if (window is null) return;
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await ViewModel.ImportSnapshotAsync(file.Path);
    }
}
