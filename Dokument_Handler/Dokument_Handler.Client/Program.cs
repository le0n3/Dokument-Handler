using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Dokument_Handler.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped<IThemeService, ThemeService>();

await builder.Build().RunAsync();
