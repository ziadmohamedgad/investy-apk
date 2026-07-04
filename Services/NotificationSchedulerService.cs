#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Investy.Mobile.Platforms.Android;
#endif

namespace Investy.Mobile.Services;

/// <summary>
/// Schedules 5 weekly AlarmManager alarms — one per workday (Sun–Thu) at 8 PM device time.
/// Uses native Android AlarmManager; no external package required.
/// Called on every app launch to restore alarms after device reboots.
/// </summary>
public class NotificationSchedulerService
{
    /// <summary>AlarmManager request codes 201–205, one per workday.</summary>
    private static readonly (int RequestCode, DayOfWeek Day)[] WorkdaySchedule =
    [
        (201, DayOfWeek.Sunday),
        (202, DayOfWeek.Monday),
        (203, DayOfWeek.Tuesday),
        (204, DayOfWeek.Wednesday),
        (205, DayOfWeek.Thursday),
    ];

    /// <summary>7 days in milliseconds — repeat interval for each alarm.</summary>
    private const long WeeklyIntervalMs = 7L * 24 * 60 * 60 * 1000;

    public async Task ScheduleWorkdayNotificationsAsync()
    {
#if ANDROID
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }

            if (status != PermissionStatus.Granted)
            {
                return;
            }

            var context = global::Android.App.Application.Context;
            var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null)
            {
                return;
            }

            // Ensure the notification channel exists before the first alarm fires
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    NotificationBroadcastReceiver.ChannelId,
                    "Investy Daily",
                    NotificationImportance.Default);
                var nm = (NotificationManager?)context.GetSystemService(Context.NotificationService);
                nm?.CreateNotificationChannel(channel);
            }

            foreach (var (requestCode, day) in WorkdaySchedule)
            {
                var broadcastIntent = new Intent(context, typeof(NotificationBroadcastReceiver));

                var pendingIntent = PendingIntent.GetBroadcast(
                    context,
                    requestCode,
                    broadcastIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

                if (pendingIntent == null)
                {
                    continue;
                }

                // Cancel any existing alarm for this day before rescheduling
                alarmManager.Cancel(pendingIntent);

                // Schedule a repeating weekly alarm (inexact on Android 6+ in Doze mode —
                // acceptable for a reminder notification; a few-minute delay is fine)
                var firstTriggerMs = GetNext8pmMs(day);
                alarmManager.SetRepeating(
                    AlarmType.RtcWakeup,
                    firstTriggerMs,
                    WeeklyIntervalMs,
                    pendingIntent);
            }
        }
        catch
        {
            // Notification scheduling is best-effort; silently ignore errors
        }
#endif
    }

    /// <summary>
    /// Returns the next 8:00 PM occurrence of <paramref name="targetDay"/> as milliseconds
    /// since the Unix epoch (UTC), suitable for AlarmManager.
    /// If today is the target day but it's already 8 PM or later, the alarm is set for next week.
    /// </summary>
    private static long GetNext8pmMs(DayOfWeek targetDay)
    {
        var now = DateTime.Now;
        int daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;

        // Same day but already past 8 PM → push to next week
        if (daysUntil == 0 && now.Hour >= 20)
        {
            daysUntil = 7;
        }

        var triggerLocal = now.Date.AddDays(daysUntil).AddHours(20); // 8:00 PM local time
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(triggerLocal.ToUniversalTime() - epoch).TotalMilliseconds;
    }
}
