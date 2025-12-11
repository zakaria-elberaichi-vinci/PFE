
using PFE.ViewModels;
using Syncfusion.Maui.Scheduler;

namespace PFE.Views;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _vm;

    public CalendarPage(CalendarViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
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