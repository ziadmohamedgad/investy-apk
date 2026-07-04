using Plugin.LocalNotification;

namespace Investy.Mobile.Services;

/// <summary>
/// Schedules 5 weekly local notifications — one per workday (Sun–Thu) at 8 PM device time.
/// Called every time the app opens so alarms are restored after device reboots.
/// </summary>
public class NotificationSchedulerService
{
    /// <summary>Notification IDs 201–205, one per workday (Sun=201 … Thu=205).</summary>
    private static readonly (int Id, DayOfWeek Day)[] WorkdaySchedule =
    [
        (201, DayOfWeek.Sunday),
        (202, DayOfWeek.Monday),
        (203, DayOfWeek.Tuesday),
        (204, DayOfWeek.Wednesday),
        (205, DayOfWeek.Thursday),
    ];

    /// <summary>
    /// Cancels any previously scheduled workday notifications and reschedules them.
    /// Safe to call on every app launch; Android OS clears AlarmManager on reboot so
    /// rescheduling ensures the alarms are always active.
    /// </summary>
    public async Task ScheduleWorkdayNotificationsAsync()
    {
        try
        {
            // Request runtime permission (required on Android 13+)
            var granted = await LocalNotificationCenter.Current.RequestNotificationPermission();
            if (!granted)
            {
                return;
            }

            // Cancel all previously scheduled workday notifications
            foreach (var (id, _) in WorkdaySchedule)
            {
                LocalNotificationCenter.Current.Cancel(id);
            }

            // Schedule one weekly recurring notification per workday
            foreach (var (id, day) in WorkdaySchedule)
            {
                var notifyAt = GetNext8pmFor(day);
                var request = new NotificationRequest
                {
                    NotificationId = id,
                    Title = "Investy",
                    Description = "محفظتك تنتظرك — اطّلع على آخر أسعار اليوم",
                    Schedule = new NotificationRequestSchedule
                    {
                        NotifyTime = notifyAt,
                        RepeatType = NotificationRepeat.Weekly
                    }
                };
                await LocalNotificationCenter.Current.Show(request);
            }
        }
        catch
        {
            // Notification scheduling is best-effort; silently ignore any errors
        }
    }

    /// <summary>
    /// Returns the next DateTime at 8:00 PM (device local time) for the given day of week.
    /// If today IS that day but it's already 8 PM or later, returns the same day next week.
    /// </summary>
    private static DateTime GetNext8pmFor(DayOfWeek targetDay)
    {
        var now = DateTime.Now;
        int daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;

        // Already past 8 PM today for this weekday -> push to next week
        if (daysUntil == 0 && now.Hour >= 20)
        {
            daysUntil = 7;
        }

        return now.Date.AddDays(daysUntil).AddHours(20); // 20:00 = 8 PM
    }
}
