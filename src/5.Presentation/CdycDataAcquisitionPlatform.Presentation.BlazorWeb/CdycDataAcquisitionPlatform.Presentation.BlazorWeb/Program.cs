using CdycDataAcquisitionPlatform.Infrastructure.DataAccess.Initialization;
using CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Endpoints;
using CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Components.Shell;
using CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformPresentationServices(builder.Configuration, builder.Environment);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
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
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapPlatformApiEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Client._Imports).Assembly);

app.Run();
