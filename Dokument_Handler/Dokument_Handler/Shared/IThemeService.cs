namespace Dokument_Handler.Shared;

public interface IThemeService
{
    string CurrentTheme { get; }
    event Action? OnThemeChanged;
    Task InitializeAsync();
    Task ToggleThemeAsync();
}
