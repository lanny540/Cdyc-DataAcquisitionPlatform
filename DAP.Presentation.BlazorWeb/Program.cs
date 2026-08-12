using DAP.Infrastructure.DataAccess.Initialization;
using DAP.Presentation.BlazorWeb.Endpoints;
using DAP.Presentation.BlazorWeb.Components.Shell;
using DAP.Presentation.BlazorWeb.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformPresentationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<PostgreSqlDatabaseInitializer>();
    await initializer.InitializeAsync();
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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapPlatformApiEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DAP.Presentation.BlazorWeb.Client._Imports).Assembly);

app.Run();
