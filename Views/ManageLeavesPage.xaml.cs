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

            _vm.NotificationRequested += (s, message) =>
            {
                LeaveConfirmationPopup.Show(message);
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.LoadAsync();
        }
    }
}
