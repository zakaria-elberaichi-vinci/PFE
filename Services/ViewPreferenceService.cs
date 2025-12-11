namespace PFE.Services
{
    public class ViewPreferenceService
    {
        private const string ViewPreferenceKey = "LeavesViewPreference";

        /// <summary>
        /// true = CalendarView, false = ListView
        /// </summary>
        public bool IsCalendarView
        {
            get => Preferences.Get(ViewPreferenceKey, false);
            set => Preferences.Set(ViewPreferenceKey, value);
        }
    }
}
