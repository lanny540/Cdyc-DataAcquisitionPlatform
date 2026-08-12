using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Presentation.AvaloniaApp.ViewModels.Pages;

namespace DAP.Presentation.AvaloniaApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _currentPage;

    [ObservableProperty] private MenuItemViewModel? _selectedTopLevelItem;

    [ObservableProperty] private MenuItemViewModel? _selectedSubmenuItem;

    [ObservableProperty] private string _submenuTitle = string.Empty;

    [ObservableProperty] private bool _isSubmenuVisible;

    [ObservableProperty] private string _topbarTitle = "设计总览";

    [ObservableProperty] private string _topbarSubtitle = "Boardease Workspace";

    public ObservableCollection<MenuItemViewModel> MenuItems { get; }

    public ObservableCollection<MenuItemViewModel> CurrentSubmenuItems { get; } = [];

    public MenuItemViewModel SettingsEntry { get; }

    public IRelayCommand<MenuItemViewModel?> SelectTopLevelCommand { get; }

    public IRelayCommand<MenuItemViewModel?> NavigateCommand { get; }

    public MainWindowViewModel(
        HomePageViewModel homePageViewModel,
        SettingsPageViewModel settingsPageViewModel,
        SystemSettingsPageViewModel systemSettingsPageViewModel)
    {
        _currentPage = homePageViewModel;
        SelectTopLevelCommand = new RelayCommand<MenuItemViewModel?>(SelectTopLevelItem);
        NavigateCommand = new RelayCommand<MenuItemViewModel?>(NavigateTo);

        MenuItems = new ObservableCollection<MenuItemViewModel>
        {
            new()
            {
                Header = "工作台",
                Caption = "Board",
                Icon = "M4 4h7v7H4V4zm9 0h7v7h-7V4zM4 13h7v7H4v-7zm9 0h7v7h-7v-7z",
                Command = SelectTopLevelCommand,
                Children =
                {
                    new MenuItemViewModel
                    {
                        Header = "设计总览",
                        Caption = "看板与关键指标",
                        Icon = "M4 4h7v7H4V4zm9 0h7v7h-7V4zM4 13h7v7H4v-7zm9 0h7v7h-7v-7z",
                        TargetPage = homePageViewModel,
                        Command = NavigateCommand
                    }
                }
            },
            new()
            {
                Header = "监控",
                Caption = "Monitor",
                Icon = "M5 5h14v10H5V5zm2 2v6h10V7H7zm3 10h4v2h-4v-2z",
                Command = SelectTopLevelCommand
            },
            new()
            {
                Header = "协作",
                Caption = "Team",
                Icon = "M12 12c2.21 0 4-1.79 4-4S14.21 4 12 4 8 5.79 8 8s1.79 4 4 4zm0 2c-3.31 0-6 2.24-6 5v1h12v-1c0-2.76-2.69-5-6-5z",
                Command = SelectTopLevelCommand
            }
        };

        SettingsEntry = new MenuItemViewModel
        {
            Header = "设置",
            Caption = "Settings",
            Icon = "M12 8.75A3.25 3.25 0 1 0 12 15.25A3.25 3.25 0 1 0 12 8.75M19.43 12.98C19.47 12.66 19.5 12.33 19.5 12C19.5 11.67 19.47 11.34 19.43 11.02L21.54 9.37C21.73 9.22 21.78 8.95 21.66 8.73L19.66 5.27C19.54 5.05 19.27 4.97 19.05 5.05L16.56 6.05C16.04 5.65 15.5 5.32 14.87 5.07L14.5 2.42C14.46 2.18 14.25 2 14 2H10C9.75 2 9.54 2.18 9.5 2.42L9.13 5.07C8.5 5.32 7.96 5.66 7.44 6.05L4.95 5.05C4.73 4.97 4.46 5.05 4.34 5.27L2.34 8.73C2.22 8.95 2.27 9.22 2.46 9.37L4.57 11.02C4.53 11.34 4.5 11.67 4.5 12C4.5 12.33 4.53 12.66 4.57 12.98L2.46 14.63C2.27 14.78 2.22 15.05 2.34 15.27L4.34 18.73C4.46 18.95 4.73 19.03 4.95 18.95L7.44 17.95C7.96 18.35 8.5 18.68 9.13 18.93L9.5 21.58C9.54 21.82 9.75 22 10 22H14C14.25 22 14.46 21.82 14.5 21.58L14.87 18.93C15.5 18.68 16.04 18.34 16.56 17.95L19.05 18.95C19.27 19.03 19.54 18.95 19.66 18.73L21.66 15.27C21.78 15.05 21.73 14.78 21.54 14.63L19.43 12.98Z",
            Command = SelectTopLevelCommand
        };
        SettingsEntry.Children.Add(new MenuItemViewModel
        {
            Header = "采集设置",
            Caption = "本地配置与同步",
            Icon = "M4 6h16v10H4V6zm0 12h10v2H4v-2zm12 0h4v2h-4v-2z",
            TargetPage = settingsPageViewModel,
            Command = NavigateCommand
        });
        SettingsEntry.Children.Add(new MenuItemViewModel
        {
            Header = "系统设置",
            Caption = "主题、版本与版权信息",
            Icon = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V5h14v14zM7 7h10v2H7zm0 4h10v2H7zm0 4h7v2H7z",
            TargetPage = systemSettingsPageViewModel,
            Command = NavigateCommand
        });

        SelectTopLevelItem(MenuItems[0]);
    }

    private void SelectTopLevelItem(MenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (MenuItemViewModel menuItem in GetTopLevelItems())
        {
            menuItem.IsSelected = ReferenceEquals(menuItem, item);
            menuItem.IsExpanded = ReferenceEquals(menuItem, item);
        }

        SelectedTopLevelItem = item;
        SubmenuTitle = item.Header;

        CurrentSubmenuItems.Clear();
        foreach (MenuItemViewModel child in item.Children)
        {
            child.IsSelected = false;
            CurrentSubmenuItems.Add(child);
        }

        IsSubmenuVisible = CurrentSubmenuItems.Count > 0;

        if (IsSubmenuVisible)
        {
            NavigateTo(CurrentSubmenuItems[0]);
        }
    }

    private void NavigateTo(MenuItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedSubmenuItem = item;

        foreach (MenuItemViewModel submenuItem in CurrentSubmenuItems)
        {
            submenuItem.IsSelected = ReferenceEquals(submenuItem, item);
        }

        if (item.TargetPage is not null)
        {
            CurrentPage = item.TargetPage;
        }

        TopbarTitle = item.Header;
        TopbarSubtitle = item.Caption;
    }

    private IEnumerable<MenuItemViewModel> GetTopLevelItems()
    {
        foreach (MenuItemViewModel item in MenuItems)
        {
            yield return item;
        }

        yield return SettingsEntry;
    }
}
