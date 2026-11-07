using Kwanteks.NicTweaker.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kwanteks.NicTweaker.Views;

public sealed class ConfigImportChoice
{
    public bool LoadUiOnly { get; init; }
    public HashSet<string> Selected { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class ConfigTransferDialogs
{
    public static async Task<ConfigImportChoice?> ShowPreviewAsync(XamlRoot xamlRoot, ConfigImportPreview preview)
    {
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var rowsPanel = new StackPanel { Spacing = 2 };
        foreach (var row in preview.Rows)
        {
            var cb = new CheckBox
            {
                IsChecked = true,
                FontSize = 11,
                MinHeight = 22,
                Content = $"{(row.Changed ? "* " : "")}{row.Section}/{row.Label}:  {row.CurrentDisplay}  →  {row.ImportedDisplay}"
            };
            checks[row.Id] = cb;
            rowsPanel.Children.Add(cb);
        }

        var counts = new TextBlock
        {
            FontSize = 12,
            Text = $"{preview.ChangedCount} changed · {preview.UnchangedCount} unchanged · {preview.Skipped.Count} skipped"
        };

        var selectAll = new Button { Content = "Select all", FontSize = 12, MinHeight = 28, Style = null };
        var selectNone = new Button { Content = "Select none", FontSize = 12, MinHeight = 28 };
        selectAll.Click += (_, _) => { foreach (var cb in checks.Values) cb.IsChecked = true; };
        selectNone.Click += (_, _) => { foreach (var cb in checks.Values) cb.IsChecked = false; };

        var warningText = RestoreDialogs.WarningBody;
        if (!string.IsNullOrWhiteSpace(preview.AdapterWarning))
            warningText = preview.AdapterWarning + "\n\n" + warningText;

        var warning = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120,
            Text = warningText
        };

        var ack = new CheckBox
        {
            Content = RestoreDialogs.AckCheckboxText,
            FontSize = 12
        };

        var panel = new StackPanel { Spacing = 8, Width = 640 };
        panel.Children.Add(counts);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { selectAll, selectNone } });
        panel.Children.Add(new ScrollViewer
        {
            Content = rowsPanel,
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        if (preview.Skipped.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Text = "Skipped:\n" + string.Join("\n", preview.Skipped.Take(12))
                    + (preview.Skipped.Count > 12 ? $"\n… +{preview.Skipped.Count - 12} more" : "")
            });
        }
        panel.Children.Add(warning);
        panel.Children.Add(ack);

        var dialog = new ContentDialog
        {
            Title = "Import preview",
            Content = panel,
            PrimaryButtonText = "Apply selected",
            SecondaryButtonText = "Load into UI only",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        ack.Checked += (_, _) => { dialog.IsPrimaryButtonEnabled = true; dialog.IsSecondaryButtonEnabled = true; };
        ack.Unchecked += (_, _) => { dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false; };

        var result = await dialog.ShowAsync();
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary))
            return null;

        return new ConfigImportChoice
        {
            LoadUiOnly = result == ContentDialogResult.Secondary,
            Selected = checks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static async Task<bool> ContinueWithoutRestoreAsync(XamlRoot xamlRoot, string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Restore point failed",
            Content = $"{message}\n\nContinue without a restore point?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
