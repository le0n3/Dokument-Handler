namespace Dokument_Handler.Shared;

/// <summary>
/// Defines the contract for reading and toggling the active UI theme.
/// </summary>
public interface IThemeService
{
    /// <summary>Gets the name of the currently active theme (e.g. <c>"light"</c> or <c>"dark"</c>).</summary>
    string CurrentTheme { get; }

    /// <summary>Raised when the active theme changes.</summary>
    event Action? OnThemeChanged;

    /// <summary>Initialises the service by reading the persisted theme from the browser. Subsequent calls are no-ops.</summary>
    Task InitializeAsync();

    /// <summary>Toggles the active theme and notifies subscribers.</summary>
    Task ToggleThemeAsync();
}
