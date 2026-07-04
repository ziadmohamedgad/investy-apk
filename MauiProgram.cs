using Investy.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace Investy.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<LocalDatabase>();
        builder.Services.AddSingleton<PortfolioService>();
        builder.Services.AddSingleton<ExportService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<BalanceVisibilityService>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton<EodhdMobileService>();
        builder.Services.AddSingleton<NotificationSchedulerService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
