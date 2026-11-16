using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAP.Clients;
using DAP.Presentation.AvaloniaApp.Services;

namespace DAP.Presentation.AvaloniaApp.ViewModels.Shell;

/// <summary>
/// 登录页视图模型。
///
/// 桌面端没有浏览器代管 Cookie，必须自己完成「表单登录 → 持有会话」这一步；
/// 首次登录被强制改密时，同一视图切换到改密表单，避免用户被迫打开浏览器才能用客户端。
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly DesktopSession _session;

    /// <summary>
    /// 初始化登录视图模型。
    /// </summary>
    /// <param name="session">桌面会话。</param>
    public LoginViewModel(DesktopSession session)
    {
        _session = session;
        LoginCommand = new AsyncRelayCommand(LoginAsync);
        ChangePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync);
        BackToLoginCommand = new AsyncRelayCommand(BackToLoginAsync);
    }

    /// <summary>
    /// 服务端地址，显示在登录卡片下方。
    /// </summary>
    public string ServerAddress => _session.ServerAddress;

    /// <summary>
    /// 账号。
    /// </summary>
    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>
    /// 密码。
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// 新密码（强制改密时使用）。
    /// </summary>
    [ObservableProperty]
    private string _newPassword = string.Empty;

    /// <summary>
    /// 确认新密码。
    /// </summary>
    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>
    /// 是否正在提交。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>
    /// 是否空闲。
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// 提示信息。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    /// <summary>
    /// 是否有提示。
    /// </summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// 是否处于改密阶段（服务端要求首次登录改密）。
    /// </summary>
    [ObservableProperty]
    private bool _needsPasswordChange;

    /// <summary>
    /// 登录命令。
    /// </summary>
    public IAsyncRelayCommand LoginCommand { get; }

    /// <summary>
    /// 改密命令。
    /// </summary>
    public IAsyncRelayCommand ChangePasswordCommand { get; }

    /// <summary>
    /// 放弃改密、退回登录表单。
    /// </summary>
    public IAsyncRelayCommand BackToLoginCommand { get; }

    /// <summary>
    /// 登录。
    /// </summary>
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            Message = "请输入账号与密码。";
            return;
        }

        IsBusy = true;
        Message = null;

        try
        {
            PlatformSignInResult result = await _session.SignInAsync(UserName.Trim(), Password);

            if (!result.Succeeded)
            {
                Message = result.ErrorMessage;
                return;
            }

            if (_session.MustChangePassword)
            {
                NeedsPasswordChange = true;
                Message = "首次登录必须修改初始密码，请设置新密码后继续。";
                return;
            }

            Password = string.Empty;
        }
        catch (Exception ex)
        {
            Message = UserFacingErrors.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 首次登录改密。
    /// </summary>
    private async Task ChangePasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            Message = "请输入新密码。";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            Message = "两次输入的新密码不一致。";
            return;
        }

        IsBusy = true;
        Message = null;

        try
        {
            var result = await _session.ChangePasswordAsync(Password, NewPassword, ConfirmPassword);

            if (!result.Succeeded)
            {
                Message = result.ErrorMessage ?? "修改密码失败。";
                return;
            }

            NeedsPasswordChange = false;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            Password = string.Empty;
        }
        catch (Exception ex)
        {
            Message = UserFacingErrors.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 放弃改密：清除会话回到登录表单，避免停留在半登录状态。
    /// </summary>
    private async Task BackToLoginAsync()
    {
        await _session.SignOutAsync();
        NeedsPasswordChange = false;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        Password = string.Empty;
        Message = null;
    }
}
