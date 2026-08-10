using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CdycDataAcquisitionPlatform.Presentation.AvaloniaApp.ViewModels;

public partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private ViewModelBase? _targetPage;

    [ObservableProperty]
    private ObservableCollection<MenuItemViewModel>? _children;

    public ICommand? Command { get; set; }
}
