using DAP.Presentation.BlazorWeb.Components.Shell;
using DAP.Presentation.BlazorWeb.Endpoints;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformPresentationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

// 密钥环位置直接打在启动日志里：掉线排查时第一个要看的就是「两次部署用的是不是同一把钥匙」。
app.Logger.LogInformation(
    "数据保护密钥目录：{KeyPath}（多实例部署必须挂同一个持久卷）",
    app.Services.GetRequiredService<DataProtectionKeyLocation>().DirectoryPath);

// 传输层提示：Secure Cookie 与反向代理配置错了，现象都很像「登录不上」，必须提前说清楚。
foreach (var (level, message) in PlatformTransportExtensions.DescribeTransportWarnings(
             app.Configuration, app.Environment))
{
    app.Logger.Log(level, "{Message}", message);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.MapOpenApi();
}
else
{
    // 宿主服务端异常统一回落到 /Error，由 ServerErrorPage 负责展示。
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

// 接口的 401/403 必须原样返回给调用方：若也走状态码页重执行，
// /api/auth/me 会被改写成跳转，WASM 端就分不清「未登录」和「页面不存在」。
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

// 必须在认证之前：白名单与限流都按还原后的 RemoteIpAddress 判定。
app.UsePlatformForwardedHeaders(app.Configuration);

// CSRF 第二道防线：SameSite=Lax 之外再校验一次来源（见 PlatformSameOriginGuard）。
app.UsePlatformSameOriginGuard();

app.UseAntiforgery();

// 限流放在认证之前：被限流拦下的请求不必再走一遍 Cookie 解密与数据库查询。
app.UseRateLimiter();

// 认证闸门必须在端点映射之前：未登录时不渲染任何业务页面外壳。
// 越权异常处理要包住端点与电路（装饰器抛 ForbiddenAccessException），放在最前。
app.UseForbiddenAccessHandling();
app.UseAuthentication();
app.UseAuthorization();
app.UsePlatformAuthenticationGate();

app.MapPlatformApiEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DAP.Presentation.BlazorWeb.Client._Imports).Assembly);

app.Run();
