using System;
using WindowsGSM.Functions;

namespace WindowsGSM
{
    public partial class MainWindow
    {
        private DateTime _backupScheduleCalendarDate = DateTime.Today;
        private BackupScheduleCalendarView _backupScheduleCalendarView = BackupScheduleCalendarView.Month;
    }
}
