using PFE.Services;
using PFE.ViewModels;

namespace PFE.Views
{
    public partial class LeavesPage : ContentPage
    {
        private readonly LeaveViewModel _vm;
        private readonly OfflineService _offlineService;

        public LeavesPage(LeaveViewModel vm, OfflineService offlineService)
        {
            InitializeComponent();
            _vm = vm;
            _offlineService = offlineService;
            BindingContext = _vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            // Vérifier si une synchronisation a eu lieu pendant qu'on était ailleurs
            if (_offlineService.HasSyncCompleted)
            {
                System.Diagnostics.Debug.WriteLine("[LeavesPage] Sync détectée, rafraîchissement forcé...");
                _offlineService.ClearSyncFlag();
            }
            
            await _vm.LoadAsync();
        }
    }
}