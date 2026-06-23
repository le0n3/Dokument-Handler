using Dokument_Handler.Components;
using Dokument_Handler.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddScoped<Dokument_Handler.Shared.IThemeService, ThemeService>();
builder.Services.AddHostedService<EmailImportService>();

// Register IHttpClientFactory and the AI classification service (scoped per request).
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiClassificationService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapControllers();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Dokument_Handler.Client._Imports).Assembly);

app.Run();
