using PFE.ViewModels;
using Syncfusion.Maui.Calendar;

namespace PFE.Views
{
    public partial class LeaveRequestPage : ContentPage
    {
        private readonly LeaveRequestViewModel _vm;
        private readonly IServiceProvider _sp;

        public LeaveRequestPage(LeaveRequestViewModel vm, IServiceProvider sp)
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = _vm;
            _sp = sp;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _vm.LoadAsync();

            // Si l'utilisateur n'a pas le droit d'accéder à cette page
            if (_vm.IsAccessDenied)
            {
                await DisplayAlert("Accès refusé", _vm.ErrorMessage, "OK");

                _ = await Navigation.PopAsync();
                return;
            }

            // Info initiale si un message existe mais pas de types de congés
            if (!string.IsNullOrWhiteSpace(_vm.ErrorMessage) && _vm.LeaveTypes.Count == 0)
            {
                await DisplayAlert("Informations", _vm.ErrorMessage, "OK");
            }
        }

        private async void SubmitButton_Clicked(object sender, EventArgs e)
        {
            (bool success, _, string? message) = await _vm.SubmitAsync();

            if (success)
            {
                await DisplayAlert("Demande envoyée", message, "OK");
                DashboardPage dashboardPage = _sp.GetRequiredService<DashboardPage>();
                await Navigation.PushAsync(dashboardPage);
            }
            else
            {
                await DisplayAlert("Erreur", message ?? "Une erreur est survenue.", "OK");
            }
        }

        private void SfCalendar_SelectionChanged(object sender, CalendarSelectionChangedEventArgs e)
        {
            if (BindingContext is LeaveRequestViewModel vm)
            {
                CalendarDateRange? range = e.NewValue as CalendarDateRange;

                vm.SelectedRange = range;
            }
        }
    }
}