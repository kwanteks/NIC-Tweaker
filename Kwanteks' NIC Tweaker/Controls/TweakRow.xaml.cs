using Kwanteks.NicTweaker.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kwanteks.NicTweaker.Controls;

public sealed partial class TweakRow : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(TweakItemViewModel), typeof(TweakRow),
            new PropertyMetadata(null));

    public TweakItemViewModel Item
    {
        get => (TweakItemViewModel)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public TweakRow()
    {
        InitializeComponent();
    }
}
