using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace DAP.Presentation.AvaloniaApp.ViewModels;

public partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _header = string.Empty;

    [ObservableProperty] private string _caption = string.Empty;

    [ObservableProperty] private string _icon = string.Empty;

    [ObservableProperty] private ViewModelBase? _targetPage;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<MenuItemViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public ICommand? Command { get; set; }
}
