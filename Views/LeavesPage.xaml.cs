using PFE.ViewModels;

namespace PFE.Views
{
    public partial class LeavesPage : ContentPage
    {
        private readonly LeaveViewModel _vm;

        public LeavesPage(LeaveViewModel vm)
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