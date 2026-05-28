using Microsoft.JSInterop;

namespace Dokument_Handler.Client.Services;

public interface IThemeService
{
    string CurrentTheme { get; }
    event Action? OnThemeChanged;
    Task InitializeAsync();
    Task ToggleThemeAsync();
}

public class ThemeService : IThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private string _currentTheme = "light";
    private bool _initialized = false;

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public string CurrentTheme => _currentTheme;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            _currentTheme = await _jsRuntime.InvokeAsync<string>("getTheme");
            Console.WriteLine($"ThemeService: Theme loaded as '{_currentTheme}'");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ThemeService Init Error: {ex.Message}");
            _currentTheme = "light";
            _initialized = true;
        }
    }

    public async Task ToggleThemeAsync()
    {
        try
        {
            Console.WriteLine($"ThemeService: Toggling theme from '{_currentTheme}'");

            var newTheme = await _jsRuntime.InvokeAsync<string>("toggleTheme");
            _currentTheme = newTheme;

            Console.WriteLine($"ThemeService: Theme toggled to '{_currentTheme}'");
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ThemeService Toggle Error: {ex.Message}");
        }
    }
}
