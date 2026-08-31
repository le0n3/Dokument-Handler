using System.Security.Claims;
using System.Threading.RateLimiting;
using Dokument_Handler.Components;
using Dokument_Handler.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddScoped<Dokument_Handler.Shared.IThemeService, ThemeService>();
builder.Services.AddHostedService<EmailImportService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiClassificationService>();
var keyDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DokumentHandler", "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("DokumentHandler")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "DokumentHandler.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("expensive", context => CreateRequestLimiter(context, 5));
    options.AddPolicy("api", context => CreateRequestLimiter(context, 60));
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapPost("/auth/login", async (
        HttpContext context,
        AuthService authService,
        [FromForm] string password,
        [FromForm] string? returnUrl) =>
    {
        if (!authService.VerifyPassword(password))
            return Results.LocalRedirect($"/login?error=1&returnUrl={Uri.EscapeDataString(SafeReturnUrl(returnUrl))}");

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Dokument-Handler")],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
        return Results.LocalRedirect(SafeReturnUrl(returnUrl));
    })
    .AllowAnonymous()
    .RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect("/login");
    })
    .RequireAuthorization();

app.MapControllers().RequireAuthorization().RequireRateLimiting("api");
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Dokument_Handler.Client._Imports).Assembly);

app.Run();

static string SafeReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl)
    && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
    && returnUrl.StartsWith('/')
    && !returnUrl.StartsWith("//", StringComparison.Ordinal)
    && !returnUrl.Contains('\\')
    && !returnUrl.Any(char.IsControl)
        ? returnUrl
        : "/";

static RateLimitPartition<string> CreateRequestLimiter(HttpContext context, int permitLimit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

public partial class Program;
