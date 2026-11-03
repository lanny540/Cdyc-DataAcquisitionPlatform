using DAP.Presentation.BlazorWeb.Components.Shell;
using DAP.Presentation.BlazorWeb.Endpoints;
using DAP.Presentation.BlazorWeb.Security;
using DAP.Presentation.BlazorWeb.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformPresentationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

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
app.UseAntiforgery();

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
