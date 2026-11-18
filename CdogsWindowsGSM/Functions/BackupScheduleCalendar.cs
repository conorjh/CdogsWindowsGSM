using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NCrontab;

namespace WindowsGSM.Functions;

public enum BackupScheduleCalendarView
{
    Day,
    Week,
    Month
}

public static class BackupScheduleCalendar
{
    public static DateTime GetPeriodStart(DateTime selectedDate, BackupScheduleCalendarView view)
    {
        DateTime date = selectedDate.Date;
        DayOfWeek firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        switch (view)
        {
            case BackupScheduleCalendarView.Day:
                return date;
            case BackupScheduleCalendarView.Week:
                return date.AddDays(-((7 + (int)date.DayOfWeek - (int)firstDayOfWeek) % 7));
            case BackupScheduleCalendarView.Month:
                DateTime firstOfMonth = new DateTime(date.Year, date.Month, 1);
                return firstOfMonth.AddDays(-((7 + (int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek) % 7));
            default:
                throw new ArgumentOutOfRangeException(nameof(view));
        }
    }

    public static int GetPeriodDayCount(BackupScheduleCalendarView view)
    {
        switch (view)
        {
            case BackupScheduleCalendarView.Day:
                return 1;
            case BackupScheduleCalendarView.Week:
                return 7;
            case BackupScheduleCalendarView.Month:
                return 42;
            default:
                throw new ArgumentOutOfRangeException(nameof(view));
        }
    }

    public static IReadOnlyList<DateTime> GetOccurrences(BackupScheduleTaskConfig task, DateTime date, int maximumCount = 6)
    {
        if (task == null)
        {
            throw new ArgumentNullException(nameof(task));
        }
        if (maximumCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }
        if (!task.Enabled)
        {
            return Array.Empty<DateTime>();
        }

        CrontabSchedule schedule = CrontabSchedule.TryParse(task.CronExpression);
        if (schedule == null)
        {
            return Array.Empty<DateTime>();
        }

        DateTime dayStart = date.Date;
        DateTime dayEnd = dayStart.AddDays(1).AddTicks(-1);
        return schedule.GetNextOccurrences(dayStart.AddMinutes(-1), dayEnd)
            .Take(maximumCount)
            .ToList();
    }
}
