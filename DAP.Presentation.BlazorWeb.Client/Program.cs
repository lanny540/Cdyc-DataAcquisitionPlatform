using DAP.Presentation.BlazorWeb.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<IPlatformApiClient, PlatformApiClient>();
builder.Services.AddMudServices();

// 认证状态：WASM 端向 /api/auth/me 取，同源请求自动带上 HttpOnly 会话 Cookie。
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, PlatformAuthenticationStateProvider>();

await builder.Build().RunAsync();
