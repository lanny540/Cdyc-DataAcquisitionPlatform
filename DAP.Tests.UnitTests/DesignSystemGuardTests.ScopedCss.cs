namespace DAP.Tests.UnitTests;

/// <summary>
/// 组件样式（scoped CSS）的守卫测试。
///
/// 背景：宿主 App.razor 只 link 了 <c>DAP.Presentation.BlazorWeb.styles.css</c>，
/// 但那个文件的第一行是 <c>@import '...Client.&lt;指纹&gt;.bundle.scp.css'</c>，
/// 客户端项目的组件样式正是从这条 @import 进来的。只看包体、不跟进 @import，
/// 很容易误判成「.razor.css 不生效」而把样式挪进全局文件。
///
/// 「样式文件没有同名组件」这类问题不用在这里守：Razor SDK 会直接报
/// <c>BLAZOR102</c>，构建就过不去。
/// </summary>
public partial class DesignSystemGuardTests
{
    /// <summary>
    /// 应用样式包的 link 不能删：删掉之后所有组件的 scoped CSS 都会静默失效——
    /// 页面照常渲染，只是样式没了，属于最难排查的那种失败。
    /// </summary>
    [Fact]
    public void App_Should_Link_Application_Styles_Bundle()
    {
        string app = ReadRepoFile("DAP.Presentation.BlazorWeb/Components/Shell/App.razor");

        Assert.Contains("DAP.Presentation.BlazorWeb.styles.css", app, StringComparison.Ordinal);
    }
}
