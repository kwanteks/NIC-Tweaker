using CommunityToolkit.Mvvm.ComponentModel;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class CpuAffinityItem : ObservableObject
{
    public int Index { get; }
    public string Label => $"CPU {Index}";

    [ObservableProperty] private bool _isChecked;

    public CpuAffinityItem(int index) => Index = index;
}
