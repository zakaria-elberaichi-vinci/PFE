
using PFE.Services;
using PFE.ViewModels;
using Syncfusion.Maui.Scheduler;

namespace PFE.Views;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _vm;
    private readonly IServiceProvider _sp;
    private readonly ViewPreferenceService _viewPreference;

    public CalendarPage(CalendarViewModel vm, IServiceProvider sp, ViewPreferenceService viewPreference)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
        _sp = sp;
        _viewPreference = viewPreference;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnViewToggled(object sender, ToggledEventArgs e)
    {
        if (!e.Value)
        {
            // Désactiver le switch pendant la navigation pour éviter les clics multiples
            if (sender is Switch sw)
            {
                sw.IsEnabled = false;
            }

            try
            {
                // Sauvegarder la préférence
                _viewPreference.IsCalendarView = false;

                LeavesPage leavesPage = _sp.GetRequiredService<LeavesPage>();
                
                // Remplacer la page actuelle au lieu de l'empiler
                INavigation nav = Navigation;
                nav.InsertPageBefore(leavesPage, this);
                await nav.PopAsync(false); // false = sans animation pour plus de fluidité
            }
            catch
            {
                // En cas d'erreur, réinitialiser le switch
                if (sender is Switch sw2)
                {
                    sw2.IsToggled = true;
                    sw2.IsEnabled = true;
                }
            }
        }
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