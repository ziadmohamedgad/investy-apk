namespace Investy.Mobile.Services;

public class ThemeService
{
    private const string ThemeKey = "investy_theme";

    public bool IsDark { get; private set; }

    public event Action? Changed;

    public void Load()
    {
        IsDark = Preferences.Default.Get(ThemeKey, false);
    }

    public void Toggle()
    {
        IsDark = !IsDark;
        Preferences.Default.Set(ThemeKey, IsDark);
        Changed?.Invoke();
    }
}
