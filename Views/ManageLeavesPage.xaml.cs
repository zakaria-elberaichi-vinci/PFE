using PFE.Models;
using PFE.ViewModels;

namespace PFE.Views
{
    public partial class ManageLeavesPage : ContentPage
    {
        private readonly ManageLeavesViewModel _vm;

        public ManageLeavesPage(ManageLeavesViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = _vm;

            _vm.NotificationRequested += async (s, message) =>
            {
                await DisplayAlert("Confirmation", message, "OK");
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _vm.LoadAsync();
            if (_vm.HasNewLeaves)
            {
                await ShowNewLeavesAlertAsync();
            }
        }

        private async Task ShowNewLeavesAlertAsync()
        {
            List<LeaveToApprove> newLeaves = _vm.NewLeaves;

            string message;
            if (newLeaves.Count == 1)
            {
                LeaveToApprove leave = newLeaves[0];
                message = $"Nouvelle demande de {leave.EmployeeName}\n" +
                          $"Du {leave.StartDate:dd/MM/yyyy} au {leave.EndDate:dd/MM/yyyy}\n" +
                          $"({leave.Days} jour(s))";
            }
            else
            {
                message = $"Vous avez {newLeaves.Count} nouvelles demandes de congé :\n\n" +
                          string.Join("\n\n", newLeaves.Select(l =>
                              $"• {l.EmployeeName}\n  Du {l.StartDate:dd/MM/yyyy} au {l.EndDate:dd/MM/yyyy}"));
            }

            await DisplayAlert("📋 Nouvelles demandes de congé", message, "OK");
        }
    }
}