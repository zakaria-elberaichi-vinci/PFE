using PFE.ViewModels;

namespace PFE.Views
{
    public partial class UserProfilePage : ContentPage
    {
        private readonly UserProfileViewModel _vm;

        public UserProfilePage(UserProfileViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            BindingContext = _vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _vm.LoadAsync();
        }
    }
}
