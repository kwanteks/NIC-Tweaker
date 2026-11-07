using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Kwanteks.NicTweaker.Controls;

[ContentProperty(Name = nameof(ContentHost))]
public sealed partial class GroupBox : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(GroupBox), new PropertyMetadata(""));

    public static readonly DependencyProperty ContentHostProperty =
        DependencyProperty.Register(nameof(ContentHost), typeof(object), typeof(GroupBox), new PropertyMetadata(null));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object ContentHost
    {
        get => GetValue(ContentHostProperty);
        set => SetValue(ContentHostProperty, value);
    }

    public GroupBox()
    {
        InitializeComponent();
    }
}
