using PFE.Services;
using PFE.ViewModels;

namespace PFE.Views
{
    public partial class LeavesPage : ContentPage
    {
        private readonly LeaveViewModel _vm;
        private readonly OfflineService _offlineService;
        private readonly IServiceProvider _sp;
        private readonly ViewPreferenceService _viewPreference;

        public LeavesPage(LeaveViewModel vm, OfflineService offlineService, IServiceProvider sp, ViewPreferenceService viewPreference)
        {
            InitializeComponent();
            _vm = vm;
            _offlineService = offlineService;
            _sp = sp;
            _viewPreference = viewPreference;
            BindingContext = _vm;
        }

        private async void OnViewToggled(object sender, ToggledEventArgs e)
        {
            if (e.Value)
            {
                if (sender is Switch sw)
                {
                    sw.IsEnabled = false;
                }

                try
                {
                    _viewPreference.IsCalendarView = true;

                    CalendarPage calendarPage = _sp.GetRequiredService<CalendarPage>();

                    INavigation nav = Navigation;
                    nav.InsertPageBefore(calendarPage, this);
                    _ = await nav.PopAsync(false); // false = sans animation pour plus de fluidité
                }
                catch
                {
                    if (sender is Switch sw2)
                    {
                        sw2.IsToggled = false;
                        sw2.IsEnabled = true;
                    }
                }
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_offlineService.HasSyncCompleted)
            {
                System.Diagnostics.Debug.WriteLine("[LeavesPage] Sync détectée, rafraîchissement forcé...");
                _offlineService.ClearSyncFlag();
            }

            await _vm.LoadAsync();
        }
    }
}