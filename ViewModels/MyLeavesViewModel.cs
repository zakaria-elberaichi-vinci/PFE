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
    public class StatusItem
    {
        public string LabelFr { get; init; } = default!;
        public string? ValueEn { get; init; }
        public override string ToString() => LabelFr;
    }

    public class YearItem
    {
        public string Label { get; init; } = default!;
        public int? Value { get; init; }
        public override string ToString() => Label;
    }

    public class MyLeavesViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;
        private readonly IDatabaseService _databaseService;

        private bool _isListBusy;
        private bool _isCalendarBusy;
        private string _listErrorMessage = string.Empty;
        private string _calendarErrorMessage = string.Empty;
        private bool _isOffline;
        private string _syncMessage = string.Empty;
        private bool _isReauthenticating = false;

        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;

        private YearItem? _selectedYearItem;
        private StatusItem? _selectedStatusItem;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MyLeavesViewModel(OdooClient odooClient, OfflineService offlineService, IDatabaseService databaseService)
        {
            _odooClient = odooClient;
            _offlineService = offlineService;
            _databaseService = databaseService;

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
                if (_selectedYearItem == value)
                {
                    return;
                }

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
                if (_selectedStatusItem == value)
                {
                    return;
                }

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
            if (IsListBusy)
            {
                return;
            }

            IsListBusy = true;
            ListErrorMessage = string.Empty;

            try
            {
                await _databaseService.InitializeAsync();

                // Recuperer l'employeeId
                int employeeId = _odooClient.session.Current.EmployeeId ?? 0;
                
                // Si pas d'employeeId depuis la session, essayer depuis la DB
                if (employeeId <= 0)
                {
                    var savedSession = await _databaseService.GetLastActiveSessionAsync();
                    if (savedSession?.EmployeeId != null)
                    {
                        employeeId = savedSession.EmployeeId.Value;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] EmployeeId={employeeId}, IsOffline={IsOffline}");

                // MODE OFFLINE : Charger depuis le cache
                if (IsOffline)
                {
                    System.Diagnostics.Debug.WriteLine("[MyLeavesViewModel] Mode offline - chargement depuis cache");
                    await LoadFromCacheAsync(employeeId);
                    return;
                }

                // MODE ONLINE : Charger depuis Odoo
                if (!IsEmployee)
                {
                    bool reauthSuccess = await TryReauthenticateAsync();
                    if (!reauthSuccess || !IsEmployee)
                    {
                        // En cas d'echec, essayer le cache
                        if (employeeId > 0)
                        {
                            await LoadFromCacheAsync(employeeId);
                        }
                        else
                        {
                            ListErrorMessage = "Veuillez vous connecter en tant qu'employe.";
                            Leaves.Clear();
                        }
                        return;
                    }
                }

                List<Leave> list;
                try
                {
                    list = await _odooClient.GetLeavesAsync(SelectedStateEn);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] Erreur serveur: {ex.Message}");
                    
                    // En cas d'erreur reseau, charger depuis le cache
                    if (employeeId > 0)
                    {
                        IsOffline = true;
                        await LoadFromCacheAsync(employeeId);
                    }
                    else
                    {
                        ListErrorMessage = $"Erreur: {ex.Message}";
                    }
                    return;
                }

                // Sauvegarder en cache
                employeeId = _odooClient.session.Current.EmployeeId ?? employeeId;
                if (employeeId > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] Sauvegarde de {list.Count} conges en cache");
                    await _databaseService.SaveLeavesAsync(employeeId, list);
                }

                // Appliquer filtre annee
                if (SelectedYearItem?.Value != null)
                {
                    list = list.Where(l => l.StartDate.Year == SelectedYearItem.Value).ToList();
                }

                Leaves.Clear();
                foreach (Leave item in list)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0)
                {
                    ListErrorMessage = "Aucun conge trouve.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] Erreur: {ex.Message}");
                ListErrorMessage = $"Erreur: {ex.Message}";
            }
            finally
            {
                IsListBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        /// <summary>
        /// Charge les conges depuis le cache SQLite
        /// </summary>
        private async Task LoadFromCacheAsync(int employeeId)
        {
            try
            {
                if (employeeId <= 0)
                {
                    // Essayer de recuperer tous les conges du cache
                    var allLeaves = await _databaseService.GetAllCachedLeavesAsync(null, SelectedYearItem?.Value);
                    
                    if (allLeaves.Count == 0)
                    {
                        ListErrorMessage = "Mode hors-ligne. Aucun conge en cache.";
                        Leaves.Clear();
                        return;
                    }

                    Leaves.Clear();
                    foreach (var leave in allLeaves)
                    {
                        Leaves.Add(leave);
                    }
                    return;
                }

                string? statusFilter = null;
                if (SelectedStatusItem != null && SelectedStatusItem.LabelFr != "(Tous)")
                {
                    statusFilter = SelectedStatusItem.LabelFr;
                }

                var cachedLeaves = await _databaseService.GetCachedLeavesAsync(employeeId, statusFilter, SelectedYearItem?.Value);
                
                System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] {cachedLeaves.Count} conges charges depuis le cache");

                Leaves.Clear();
                foreach (var leave in cachedLeaves)
                {
                    Leaves.Add(leave);
                }

                if (Leaves.Count == 0)
                {
                    ListErrorMessage = "Mode hors-ligne. Aucun conge en cache.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MyLeavesViewModel] Erreur cache: {ex.Message}");
                ListErrorMessage = "Impossible de charger depuis le cache.";
            }
        }

        private async Task LoadCalendarAsync()
        {
            if (IsCalendarBusy)
            {
                return;
            }

            IsCalendarBusy = true;
            CalendarErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    bool reauthSuccess = await TryReauthenticateAsync();
                    if (!reauthSuccess || !IsEmployee)
                    {
                        CalendarErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                        TotalAllocated = 0;
                        TotalTaken = 0;
                        TotalRemaining = 0;
                        Appointments.Clear();
                        return;
                    }
                }

                List<AllocationSummary> summaries;
                List<Leave> leaves;

                try
                {
                    summaries = await _odooClient.GetAllocationsSummaryAsync();
                    leaves = await _odooClient.GetLeavesAsync();
                }
                catch (Exception ex) when (IsSessionExpiredError(ex))
                {
                    System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Session expirée (calendrier), tentative de ré-authentification...");
                    bool reauthSuccess = await TryReauthenticateAsync();

                    if (reauthSuccess)
                    {
                        summaries = await _odooClient.GetAllocationsSummaryAsync();
                        leaves = await _odooClient.GetLeavesAsync();
                    }
                    else
                    {
                        CalendarErrorMessage = "Session expirée. Veuillez vous reconnecter.";
                        TotalAllocated = 0;
                        TotalTaken = 0;
                        TotalRemaining = 0;
                        Appointments.Clear();
                        return;
                    }
                }

                TotalAllocated = summaries.Sum(s => s.TotalAllocated);
                TotalTaken = summaries.Sum(s => s.TotalTaken);
                TotalRemaining = summaries.Sum(s => s.TotalRemaining);

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

        #region Ré-authentification

        /// <summary>
        /// Vérifie si l'erreur est une erreur de session expirée Odoo
        /// </summary>
        private static bool IsSessionExpiredError(Exception ex)
        {
            string message = ex.Message.ToLowerInvariant();
            return message.Contains("session expired") ||
                   message.Contains("sessionexpired") ||
                   message.Contains("session_expired") ||
                   message.Contains("odoo session expired") ||
                   message.Contains("non authentifié");
        }

        /// <summary>
        /// Tente de se ré-authentifier avec les credentials sauvegardés
        /// </summary>
        private async Task<bool> TryReauthenticateAsync()
        {
            if (_isReauthenticating)
            {
                System.Diagnostics.Debug.WriteLine("MyLeavesViewModel: Ré-authentification déjà en cours");
                return false;
            }

            _isReauthenticating = true;

            try
            {
                string login = Preferences.Get("auth.login", string.Empty);
                string? password = null;

                try
                {
                    password = await SecureStorage.GetAsync("auth.password");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Erreur lecture SecureStorage - {ex.Message}");
                }

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    System.Diagnostics.Debug.WriteLine("MyLeavesViewModel: Credentials non disponibles pour la ré-authentification");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Tentative de ré-authentification pour {login}...");

                bool success = await _odooClient.LoginAsync(login, password);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Ré-authentification réussie pour {login}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Ré-authentification échouée pour {login}");
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MyLeavesViewModel: Exception lors de la ré-authentification - {ex.Message}");
                return false;
            }
            finally
            {
                _isReauthenticating = false;
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
                        _ = await MainThread.InvokeOnMainThreadAsync(() => SyncMessage = string.Empty);
                    });
                });
            }
            else if (e.PendingCount > 0)
            {
                _ = await MainThread.InvokeOnMainThreadAsync(() =>
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