using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kwanteks.NicTweaker.Views;

public static class SafetyDialogs
{
    /// <summary>Same substance as installer/DISCLAIMER.txt, plus safe-apply guidance.</summary>
    public const string InstallerStyleDisclaimer =
        "Changing network adapter settings can cause connection loss, instability, or in rare cases leave your network adapter unusable.\n\n" +
        "Before using this software:\n\n" +
        "• Download the latest driver for your network adapter from the manufacturer's website (Intel, Realtek, Killer, Qualcomm, MediaTek, etc.) and save it somewhere you can reach offline, such as a USB drive or another PC. If your adapter stops working you will not be able to download it afterwards.\n" +
        "• Know how to reinstall that driver manually from Device Manager.\n" +
        "• A restore point may not be able to repair every problem, especially driver or firmware level damage.\n\n" +
        "Do not mass-apply NIC settings you find online or in guides. Every driver, adapter, and system behaves differently. Apply changes in small batches or one at a time, and test after each change (latency, stability, throughput, disconnects) before adding more. If something breaks, restore the previous value before continuing.\n\n" +
        "You are responsible for your own network adapter and your system. This tool is provided as is, without warranty of any kind. The author and contributors are not liable for any damage, data loss, loss of connectivity, or hardware or driver malfunction resulting from its use.";

    public const string WarningBody =
        "Do not mass-apply NIC settings you find online or in guides. Every driver, adapter, and system behaves differently. Apply changes in small batches or one at a time, and test after each change (latency, stability, throughput, disconnects) before adding more. If something breaks, restore the previous value before continuing.";

    public const string BulkKeyLine =
        "Apply in small batches and test after each change.";

    public const string AcceptCheckboxText =
        "I have read and accept this disclaimer. I understand I am responsible for my network adapter and system.";

    /// <summary>First-launch gate (installer-style). Requires accept checkbox.</summary>
    public static async Task<bool> ShowFirstLaunchAsync(XamlRoot xamlRoot)
    {
        var ack = new CheckBox
        {
            Content = AcceptCheckboxText,
            FontSize = 12,
            IsChecked = false
        };

        var scroll = new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = InstallerStyleDisclaimer,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            }
        };

        var root = new StackPanel { Spacing = 12, MaxWidth = 520 };
        root.Children.Add(scroll);
        root.Children.Add(ack);

        var dialog = new ContentDialog
        {
            Title = "Important disclaimer",
            Content = root,
            PrimaryButtonText = "I accept",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        ack.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = ack.IsChecked == true;
        ack.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && ack.IsChecked == true;
    }

    /// <summary>
    /// Bulk-apply confirm. Primary = apply one at a time (cancel bulk). Secondary = apply this batch anyway.
    /// Returns true to proceed with the bulk write.
    /// </summary>
    public static async Task<bool> ConfirmBulkApplyAsync(XamlRoot xamlRoot, string context)
    {
        var body = new StackPanel { Spacing = 10, MaxWidth = 460 };
        body.Children.Add(new TextBlock
        {
            Text = BulkKeyLine,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        body.Children.Add(new TextBlock
        {
            Text = WarningBody,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        if (!string.IsNullOrWhiteSpace(context))
        {
            body.Children.Add(new TextBlock
            {
                Text = context,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.85
            });
        }

        var dialog = new ContentDialog
        {
            Title = "Bulk apply",
            Content = body,
            PrimaryButtonText = "Apply one at a time",
            SecondaryButtonText = "Apply this batch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Secondary;
    }
}
