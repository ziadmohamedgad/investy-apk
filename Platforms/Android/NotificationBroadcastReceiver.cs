using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace Investy.Mobile.Platforms.Android;

/// <summary>
/// Fired by AlarmManager at 8 PM on each workday. Shows a local notification
/// prompting the user to check their portfolio.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class NotificationBroadcastReceiver : BroadcastReceiver
{
    internal const string ChannelId = "investy_daily_reminder";
    internal const int NotificationId = 301;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null)
        {
            return;
        }

        // Create notification channel (required on Android 8+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Investy Daily",
                NotificationImportance.Default)
            {
                Description = "Daily portfolio reminder"
            };
            var nm = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            nm?.CreateNotificationChannel(channel);
        }

        // Tapping the notification opens the app
        var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
        PendingIntent? pendingIntent = null;
        if (launchIntent != null)
        {
            launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            pendingIntent = PendingIntent.GetActivity(
                context,
                0,
                launchIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle("Investy")
            .SetContentText("محفظتك تنتظرك — اطّلع على آخر أسعار اليوم")
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetContentIntent(pendingIntent)
            .Build();

        NotificationManagerCompat.From(context).Notify(NotificationId, notification);
    }
}
