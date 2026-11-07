using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kwanteks.NicTweaker.Views;

public sealed class RestoreWarningDialogResult
{
    public bool Accepted { get; init; }
    public bool DontShowAgain { get; init; }
}

public static class RestoreDialogs
{
    private const string WarningBody =
        "Changing network adapter settings can cause connection loss, instability, or in rare cases leave your network adapter unusable.\n\n" +
        "Before creating a restore point and applying any tweaks:\n" +
        "• Download the latest driver for your network adapter from the manufacturer's website (Intel, Realtek, Killer, Qualcomm, MediaTek, etc.) and save it somewhere you can reach offline, such as a USB drive or another PC. If your adapter stops working you will not be able to download it afterwards.\n" +
        "• Know how to reinstall that driver manually from Device Manager.\n" +
        "• A restore point may not be able to repair every problem, especially driver or firmware level damage.\n\n" +
        "You are responsible for your own network adapter and your system. This tool is provided as is, without warranty of any kind. The author and contributors are not liable for any damage, data loss, loss of connectivity, or hardware or driver malfunction resulting from its use.";

    public static async Task<RestoreWarningDialogResult> ShowFullWarningAsync(
        XamlRoot xamlRoot,
        IRestorePointService restorePoints,
        Func<AdapterInfo?> getAdapter)
    {
        var ack = new CheckBox
        {
            Content = "I have a backup of my network driver and I understand that I am responsible for my network adapter.",
            FontSize = 12
        };
        var dontShow = new CheckBox
        {
            Content = "Don't show full warning again",
            FontSize = 12
        };
        Brush muted = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 154, 154, 154));
        if (Application.Current.Resources.TryGetValue("TweakerMutedTextBrush", out var mutedObj) && mutedObj is Brush b)
            muted = b;
        var exportStatus = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted
        };
        var exportBtn = new Button
        {
            Content = "Export my current NIC driver",
            FontSize = 12,
            MinHeight = 28,
            Padding = new Thickness(12, 4, 12, 4)
        };
        exportBtn.Click += async (_, _) =>
        {
            exportBtn.IsEnabled = false;
            exportStatus.Text = "Exporting driver…";
            try
            {
                var adapter = getAdapter();
                if (adapter is null)
                {
                    exportStatus.Text = "Select an adapter first.";
                    return;
                }
                var result = await restorePoints.ExportAdapterDriverAsync(
                    adapter.PnpDeviceId, adapter.InterfaceDescription);
                exportStatus.Text = result.Message;
            }
            catch (Exception ex)
            {
                exportStatus.Text = $"Driver export failed: {ex.Message}";
            }
            finally
            {
                exportBtn.IsEnabled = true;
            }
        };

        var openDm = new HyperlinkButton
        {
            Content = "Open Device Manager",
            FontSize = 12,
            Padding = new Thickness(0)
        };
        openDm.Click += (_, _) => restorePoints.OpenDeviceManager();

        var body = new TextBlock
        {
            Text = WarningBody,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { openDm, exportBtn }
        });
        root.Children.Add(exportStatus);
        root.Children.Add(ack);
        root.Children.Add(dontShow);

        var dialog = new ContentDialog
        {
            Title = "Before you continue",
            Content = root,
            PrimaryButtonText = "Create restore point",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        ack.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = ack.IsChecked == true;
        ack.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;

        var result = await dialog.ShowAsync();
        return new RestoreWarningDialogResult
        {
            Accepted = result == ContentDialogResult.Primary && ack.IsChecked == true,
            DontShowAgain = dontShow.IsChecked == true
        };
    }

    public static async Task<bool> ShowShortConfirmAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm",
            Content = "Create a Windows restore point and continue?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<bool> ShowContinueWithoutAsync(XamlRoot xamlRoot, string failureMessage)
    {
        var dialog = new ContentDialog
        {
            Title = "Restore point failed",
            Content = $"{failureMessage}\n\nContinue without a restore point?",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task<bool> ShowEnableProtectionAsync(XamlRoot xamlRoot, string message)
    {
        var dialog = new ContentDialog
        {
            Title = "System Protection disabled",
            Content = $"{message}\n\nEnable System Protection on the system drive and retry?",
            PrimaryButtonText = "Enable System Protection",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
