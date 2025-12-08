using PFE.ViewModels;

namespace PFE.Views
{
    public partial class LeaveRequestPage : ContentPage
    {
        private readonly LeaveRequestViewModel _vm;

        public LeaveRequestPage(LeaveRequestViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = _vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _vm.LoadAsync();

            if (_vm.IsAccessDenied)
            {
                await DisplayAlert("Accès refusé", _vm.ErrorMessage, "OK");
                await Navigation.PopAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_vm.ErrorMessage) && _vm.LeaveTypes.Count == 0)
            {
                await DisplayAlert("Informations", _vm.ErrorMessage, "OK");
            }
        }

        private async void SubmitButton_Clicked(object sender, EventArgs e)
        {
            (bool success, int? createdId, string? message) = await _vm.SubmitAsync();

            if (success)
            {
                await DisplayAlert("Demande envoyée", message, "OK");
                await Navigation.PopAsync();
            }
            else
            {
                await DisplayAlert("Erreur", message, "OK");
            }
        }
    }
}