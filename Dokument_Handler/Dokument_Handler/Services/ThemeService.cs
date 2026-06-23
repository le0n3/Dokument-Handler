using Microsoft.JSInterop;
using Dokument_Handler.Shared;

namespace Dokument_Handler.Services;

/// <summary>
/// Server-side implementation of <see cref="IThemeService"/>.
/// Reads and toggles the active UI theme via JavaScript interop.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<ThemeService> _logger;
    private string _currentTheme = "light";
    private bool _initialized;

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime jsRuntime, ILogger<ThemeService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <summary>Gets the name of the currently active theme (e.g. <c>"light"</c> or <c>"dark"</c>).</summary>
    public string CurrentTheme => _currentTheme;

    /// <summary>
    /// Reads the persisted theme from the browser and initialises the service.
    /// Subsequent calls are no-ops.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            _currentTheme = await _jsRuntime.InvokeAsync<string>("getTheme");
            _logger.LogDebug("ThemeService: theme loaded as '{Theme}'.", _currentTheme);
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ThemeService: failed to load theme from browser; defaulting to 'light'.");
            _currentTheme = "light";
            _initialized = true;
        }
    }

    /// <summary>Toggles between the light and dark theme and notifies subscribers.</summary>
    public async Task ToggleThemeAsync()
    {
        try
        {
            var newTheme = await _jsRuntime.InvokeAsync<string>("toggleTheme");
            _logger.LogDebug("ThemeService: theme toggled from '{Old}' to '{New}'.", _currentTheme, newTheme);
            _currentTheme = newTheme;
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ThemeService: failed to toggle theme.");
        }
    }
}
