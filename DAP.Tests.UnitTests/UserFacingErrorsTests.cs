using WebErrors = DAP.Presentation.BlazorWeb.Client.UserFacingErrors;

namespace DAP.Tests.UnitTests;

/// <summary>
/// 用户可见错误文案的规则校验。
///
/// 核心约束：业务异常的中文消息要原样展示，框架异常的英文消息必须换成兜底文案——
/// 后者曾把 EF 的「A second operation was started on this context instance…」
/// 直接渲染进审计页的错误条里。桌面端 `DAP.Presentation.AvaloniaApp.Common.UserFacingErrors`
/// 与这里规则一致（测试项目不引用 Avalonia，靠同一份实现约定对齐）。
/// </summary>
public sealed class UserFacingErrorsTests
{
    [Fact]
    public void Timeout_Should_Return_Chinese_Conclusion()
    {
        Assert.Equal("操作超时，请稍后重试。", WebErrors.Describe(new OperationCanceledException()));
    }

    [Fact]
    public void Business_Message_Should_Be_Shown_As_Is()
    {
        Assert.Equal(
            "账号 p2a_user 已存在。",
            WebErrors.Describe(new InvalidOperationException("账号 p2a_user 已存在。")));
    }

    [Fact]
    public void Framework_Message_Should_Fall_Back()
    {
        Assert.Equal(
            "操作失败，请查看服务端日志。",
            WebErrors.Describe(new InvalidOperationException(
                "A second operation was started on this context instance before a previous operation completed.")));
    }

    [Fact]
    public void Custom_Fallback_Should_Be_Used()
    {
        Assert.Equal(
            "审计记录加载失败。",
            WebErrors.Describe(new InvalidOperationException("The operation is not valid."), "审计记录加载失败。"));
    }

    [Fact]
    public void Network_Error_Should_Return_Chinese_Conclusion()
    {
        Assert.Equal(
            "网络请求失败，请检查网络连接后重试。",
            WebErrors.Describe(new HttpRequestException("Connection refused")));
    }

    [Fact]
    public void Unknown_Exception_Should_Not_Leak_Message()
    {
        Assert.Equal(
            "操作失败，请查看服务端日志。",
            WebErrors.Describe(new ArgumentException("No such host is known.")));
    }
}
