using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Helpers;
using PFE.Models;
using PFE.Services;
using Syncfusion.Maui.Scheduler;
using static PFE.Helpers.LeaveStatusHelper;

namespace PFE.ViewModels
{
    public class MyLeavesViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;

        // État de chargement
        private bool _isListBusy;
        private bool _isCalendarBusy;
        private string _listErrorMessage = string.Empty;
        private string _calendarErrorMessage = string.Empty;
        private bool _isOffline;
        private string _syncMessage = string.Empty;

        // Données calendrier
        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;

        // Filtres
        private YearItem? _selectedYearItem;
        private StatusItem? _selectedStatusItem;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MyLeavesViewModel(OdooClient odooClient, OfflineService offlineService)
        {
            _odooClient = odooClient;
            _offlineService = offlineService;

            Leaves = [];
            Appointments = [];
            Years = [];
            Statuses = [];

            RefreshCommand = new RelayCommand(async _ => await LoadAsync(false), _ => !IsListBusy && !IsCalendarBusy);

            InitializeYears();
            InitializeStatuses();

            _offlineService.SyncStatusChanged += OnSyncStatusChanged;
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }

        #region Propriétés communes

        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public bool IsOffline
        {
            get => _isOffline;
            private set { _isOffline = value; OnPropertyChanged(); }
        }

        public string SyncMessage
        {
            get => _syncMessage;
            private set { _syncMessage = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }

        #endregion

        #region Propriétés Vue Liste

        public ObservableCollection<Leave> Leaves { get; }
        public ObservableCollection<YearItem> Years { get; }
        public ObservableCollection<StatusItem> Statuses { get; }

        public YearItem? SelectedYearItem
        {
            get => _selectedYearItem;
            set
            {
                if (_selectedYearItem == value) return;
                _selectedYearItem = value;
                OnPropertyChanged();
                if (!IsListBusy)
                {
                    _ = LoadListAsync();
                }
            }
        }

        public StatusItem? SelectedStatusItem
        {
            get => _selectedStatusItem;
            set
            {
                if (_selectedStatusItem == value) return;
                _selectedStatusItem = value;
                OnPropertyChanged();
                if (!IsListBusy)
                {
                    _ = LoadListAsync();
                }
            }
        }

        public string? SelectedStateEn => SelectedStatusItem?.ValueEn;

        public bool IsListBusy
        {
            get => _isListBusy;
            private set
            {
                _isListBusy = value;
                OnPropertyChanged();
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string ListErrorMessage
        {
            get => _listErrorMessage;
            private set { _listErrorMessage = value; OnPropertyChanged(); }
        }

        #endregion

        #region Propriétés Vue Calendrier

        public ObservableCollection<SchedulerAppointment> Appointments { get; }

        public int TotalAllocated
        {
            get => _totalAllocated;
            private set { _totalAllocated = value; OnPropertyChanged(); }
        }

        public int TotalTaken
        {
            get => _totalTaken;
            private set { _totalTaken = value; OnPropertyChanged(); }
        }

        public int TotalRemaining
        {
            get => _totalRemaining;
            private set { _totalRemaining = value; OnPropertyChanged(); }
        }

        public bool IsCalendarBusy
        {
            get => _isCalendarBusy;
            private set
            {
                _isCalendarBusy = value;
                OnPropertyChanged();
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string CalendarErrorMessage
        {
            get => _calendarErrorMessage;
            private set { _calendarErrorMessage = value; OnPropertyChanged(); }
        }

        #endregion

        #region Méthodes de chargement

        public async Task LoadAsync(bool isCalendarView)
        {
            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

            if (isCalendarView)
            {
                await LoadCalendarAsync();
            }
            else
            {
                await LoadListAsync();
            }
        }

        private async Task LoadListAsync()
        {
            if (IsListBusy) return;

            IsListBusy = true;
            ListErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    ListErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    Leaves.Clear();
                    return;
                }

                if (IsOffline)
                {
                    ListErrorMessage = "Mode hors-ligne. Connectez-vous pour voir vos congés.";
                    return;
                }

                List<Leave> list = await _odooClient.GetLeavesAsync(SelectedStateEn);

                Leaves.Clear();
                foreach (Leave item in list)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0)
                {
                    ListErrorMessage = "Aucun congé trouvé.";
                }
            }
            catch (Exception ex)
            {
                ListErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
            }
            finally
            {
                IsListBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        private async Task LoadCalendarAsync()
        {
            if (IsCalendarBusy) return;

            IsCalendarBusy = true;
            CalendarErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    CalendarErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    TotalAllocated = 0;
                    TotalTaken = 0;
                    TotalRemaining = 0;
                    Appointments.Clear();
                    return;
                }

                List<AllocationSummary> summaries = await _odooClient.GetAllocationsSummaryAsync();

                TotalAllocated = summaries.Sum(s => s.TotalAllocated);
                TotalTaken = summaries.Sum(s => s.TotalTaken);
                TotalRemaining = summaries.Sum(s => s.TotalRemaining);

                List<Leave> leaves = await _odooClient.GetLeavesAsync();

                Appointments.Clear();

                foreach (Leave leave in leaves)
                {
                    string colorHex = LeaveTypeHelper.GetColorHex(leave.Type);
                    Brush background = new SolidColorBrush(Color.FromArgb(colorHex));

                    string notes = $"Statut: {leave.Status}\n" +
                                   (leave.FirstApprover != null ? $"Approbateur: {leave.FirstApprover}\n" : "");

                    Appointments.Add(new SchedulerAppointment
                    {
                        Subject = $"{leave.Type} : {leave.Days} {(leave.Days > 1 ? "jours" : "jour")}",
                        StartTime = leave.StartDate,
                        EndTime = leave.EndDate,
                        IsAllDay = true,
                        Background = background,
                        Notes = notes,
                    });
                }
            }
            catch (Exception ex)
            {
                CalendarErrorMessage = $"Impossible de charger les données : {ex.Message}";
                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
                Appointments.Clear();
            }
            finally
            {
                IsCalendarBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        #endregion

        #region Initialisation des filtres

        private void InitializeYears()
        {
            Years.Clear();
            int y = DateTime.Now.Year;

            Years.Add(new YearItem { Label = "(Toutes)", Value = null });
            Years.Add(new YearItem { Label = (y - 1).ToString(), Value = y - 1 });
            Years.Add(new YearItem { Label = y.ToString(), Value = y });
            Years.Add(new YearItem { Label = (y + 1).ToString(), Value = y + 1 });

            SelectedYearItem = Years.FirstOrDefault(it => it.Value == y);
        }

        private void InitializeStatuses()
        {
            Statuses.Clear();
            Statuses.Add(new StatusItem { LabelFr = "(Tous)", ValueEn = null });

            foreach (KeyValuePair<string, string> kv in FrenchToEnglishStatus)
            {
                Statuses.Add(new StatusItem { LabelFr = kv.Key, ValueEn = kv.Value });
            }

            SelectedStatusItem = Statuses[0];
        }

        #endregion

        #region Événements

        private async void OnSyncStatusChanged(object? sender, SyncStatusEventArgs e)
        {
            if (e.IsComplete && e.SuccessCount > 0)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    SyncMessage = $"? {e.SuccessCount} congé{(e.SuccessCount > 1 ? "s synchronisé" : " synchronisé")}s";
                    await Task.Delay(1500);
                    await LoadListAsync();

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        await MainThread.InvokeOnMainThreadAsync(() => SyncMessage = string.Empty);
                    });
                });
            }
            else if (e.PendingCount > 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    SyncMessage = $"? {e.PendingCount} demande{(e.PendingCount > 1 ? "s" : "")} en attente");
            }
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                IsOffline = e.NetworkAccess != NetworkAccess.Internet;

                if (e.NetworkAccess == NetworkAccess.Internet)
                {
                    await LoadListAsync();
                }
            });
        }

        #endregion

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
