using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CdycDataAcquisitionPlatform.Presentation.BlazorWeb.Client.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<IPlatformApiClient, PlatformApiClient>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
