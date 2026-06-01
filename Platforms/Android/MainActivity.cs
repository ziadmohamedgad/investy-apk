using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace Investy.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBars();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplySystemBars();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplySystemBars();
        }
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplySystemBars();
    }

    private void ApplySystemBars()
    {
        var isDark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var background = Android.Graphics.Color.ParseColor(isDark ? "#0F172A" : "#F5F7FB");

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window?.SetDecorFitsSystemWindows(false);
        }

        Window?.SetStatusBarColor(background);
        Window?.SetNavigationBarColor(background);

        if (Window == null)
        {
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
        {
            var attributes = Window.Attributes;
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attributes;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            Window.StatusBarContrastEnforced = false;
            Window.NavigationBarContrastEnforced = false;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window.InsetsController?.SetSystemBarsAppearance(
                isDark ? 0 : (int)WindowInsetsControllerAppearance.LightStatusBars,
                (int)WindowInsetsControllerAppearance.LightStatusBars);
            Window.InsetsController?.Hide(WindowInsets.Type.StatusBars());
            Window.InsetsController!.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            return;
        }

#pragma warning disable CA1422
        Window.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
        var flags = SystemUiFlags.LayoutStable
            | SystemUiFlags.LayoutFullscreen
            | SystemUiFlags.Fullscreen
            | SystemUiFlags.ImmersiveSticky;
        if (!isDark)
        {
            flags |= SystemUiFlags.LightStatusBar;
        }
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
#pragma warning restore CA1422
    }
}
