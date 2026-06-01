namespace Investy.Mobile.Services;

public class BalanceVisibilityService
{
    private const string HideKey = "investy_hide_balances";

    public bool HideBalances { get; private set; }

    public event Action? Changed;

    public void Load()
    {
        HideBalances = Preferences.Default.Get(HideKey, false);
    }

    public void Toggle()
    {
        HideBalances = !HideBalances;
        Preferences.Default.Set(HideKey, HideBalances);
        Changed?.Invoke();
    }
}
