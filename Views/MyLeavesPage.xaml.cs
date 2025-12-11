using PFE.Services;
using PFE.ViewModels;
using Syncfusion.Maui.Scheduler;

namespace PFE.Views
{
    public partial class MyLeavesPage : ContentPage
    {
        private readonly MyLeavesViewModel _vm;
        private readonly ViewPreferenceService _viewPreference;

        public MyLeavesPage(MyLeavesViewModel vm, ViewPreferenceService viewPreference)
        {
            InitializeComponent();
            _vm = vm;
            _viewPreference = viewPreference;
            BindingContext = _vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            bool isCalendarView = _viewPreference.IsCalendarView;
            ViewSwitch.IsToggled = isCalendarView;
            UpdateViewState(isCalendarView);

            await _vm.LoadAsync(isCalendarView);
        }

        private void OnViewToggled(object sender, ToggledEventArgs e)
        {
            _viewPreference.IsCalendarView = e.Value;

            UpdateViewState(e.Value);

            _ = _vm.LoadAsync(e.Value);
        }

        private void UpdateViewState(bool isCalendarView)
        {
            ListLabel.FontAttributes = isCalendarView ? FontAttributes.None : FontAttributes.Bold;
            ListLabel.TextColor = isCalendarView ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#60A5FA");

            CalendarLabel.FontAttributes = isCalendarView ? FontAttributes.Bold : FontAttributes.None;
            CalendarLabel.TextColor = isCalendarView ? Color.FromArgb("#60A5FA") : Color.FromArgb("#9CA3AF");

            StatsFrame.IsVisible = isCalendarView;
            ListViewContainer.IsVisible = !isCalendarView;
            CalendarViewContainer.IsVisible = isCalendarView;
        }

        private async void OnSchedulerTapped(object sender, SchedulerTappedEventArgs e)
        {
            if (e.Appointments != null && e.Appointments.Count > 0)
            {
                SchedulerAppointment appointment = (SchedulerAppointment)e.Appointments[0];

                await DisplayAlert(
                    "Détails du congé",
                    $"{appointment.Subject}\n" +
                    $"Début: {appointment.StartTime:dd/MM/yyyy}\n" +
                    $"Fin: {appointment.EndTime:dd/MM/yyyy}\n" +
                    $"{appointment.Notes}",
                    "OK");
            }
        }
    }
}