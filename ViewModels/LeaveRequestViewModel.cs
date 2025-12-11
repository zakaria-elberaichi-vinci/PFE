using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Models.Database;
using PFE.Services;
using Syncfusion.Maui.Calendar;
using static PFE.Helpers.DateHelper;

namespace PFE.ViewModels
{
    public class LeaveRequestViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;
        private readonly IDatabaseService _databaseService;

        private int _employeeId;

        private CalendarDateRange? _selectedRange;

        private ObservableCollection<LeaveTypeItem> _leaveTypes = [];

        private readonly HashSet<DateTime> _blockedDatesSet = [];

        private LeaveTypeItem? _selectedLeaveType;

        private string _reason = string.Empty;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _validationMessage = string.Empty;
        private bool _isAccessDenied;
        private bool _hasOverlap;
        private bool _isSyncing;
        private string _syncMessage = string.Empty;
        private bool _showSyncStatus;
        private bool _isOffline;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveRequestViewModel(OdooClient odooClient, OfflineService offlineService, IDatabaseService databaseService)
        {
            _odooClient = odooClient;
            _offlineService = offlineService;
            _databaseService = databaseService;

            SelectedRange = new CalendarDateRange(DateTime.Today, null);

            SubmitCommand = new RelayCommand(
                async _ => await SubmitAsync(),
                _ => CanSubmit
            );

            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
            _offlineService.SyncStatusChanged += OnSyncStatusChanged;
        }

        public CalendarDateRange? SelectedRange
        {
            get => _selectedRange;
            set
            {
                _selectedRange = value;
                ValidationMessage = string.Empty;
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(SelectedRange));
                OnPropertyChanged(nameof(AreDatesValid));
                OnPropertyChanged(nameof(CanSubmit));
                if (value?.StartDate != null && value?.EndDate != null)
                {
                    _ = OnDatesChangedAsync();
                }
            }
        }

        public ObservableCollection<LeaveTypeItem> LeaveTypes
        {
            get => _leaveTypes;
            private set
            {
                if (Set(ref _leaveTypes, value))
                {
                    OnPropertyChanged(nameof(LeaveTypes));
                }
            }
        }

        public Func<DateTime, bool>? SelectableDayPredicate { get; private set; }

        public LeaveTypeItem? SelectedLeaveType
        {
            get => _selectedLeaveType;
            set
            {
                if (Set(ref _selectedLeaveType, value))
                {
                    OnPropertyChanged(nameof(SelectedLeaveType));
                    OnPropertyChanged(nameof(CanSubmit));
                    UpdateValidationMessage();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Reason
        {
            get => _reason;
            set
            {
                if (Set(ref _reason, value))
                {
                    OnPropertyChanged(nameof(Reason));
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(CanSubmit));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (Set(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (Set(ref _validationMessage, value))
                {
                    OnPropertyChanged(nameof(ValidationMessage));
                }
            }
        }

        public bool HasOverlap
        {
            get => _hasOverlap;
            private set
            {
                if (Set(ref _hasOverlap, value))
                {
                    OnPropertyChanged(nameof(HasOverlap));
                    OnPropertyChanged(nameof(CanSubmit));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;
        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public bool IsAccessDenied
        {
            get => _isAccessDenied;
            private set
            {
                if (Set(ref _isAccessDenied, value))
                {
                    OnPropertyChanged(nameof(IsAccessDenied));
                }
            }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            private set
            {
                if (Set(ref _isSyncing, value))
                {
                    OnPropertyChanged(nameof(IsSyncing));
                }
            }
        }

        public string SyncMessage
        {
            get => _syncMessage;
            private set
            {
                if (Set(ref _syncMessage, value))
                {
                    OnPropertyChanged(nameof(SyncMessage));
                    ShowSyncStatus = !string.IsNullOrEmpty(value) && !IsSyncing;
                }
            }
        }

        public bool ShowSyncStatus
        {
            get => _showSyncStatus;
            private set
            {
                if (Set(ref _showSyncStatus, value))
                {
                    OnPropertyChanged(nameof(ShowSyncStatus));
                }
            }
        }

        public bool IsOffline
        {
            get => _isOffline;
            private set
            {
                if (Set(ref _isOffline, value))
                {
                    OnPropertyChanged(nameof(IsOffline));
                }
            }
        }

        public bool AreDatesValid => SelectedRange?.StartDate != null && SelectedRange?.EndDate != null;

        public bool CanSubmit =>
            !IsBusy &&
            AreDatesValid &&
            SelectedLeaveType is not null &&
            !HasOverlap &&
            !(SelectedLeaveType.RequiresAllocation && (SelectedLeaveType.Days ?? 0) <= 0);

        public ICommand SubmitCommand { get; }

        public async Task LoadAsync()
        {
            ErrorMessage = string.Empty;

            // Vérifier la connectivité au démarrage
            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

            if (!IsEmployee)
            {
                IsAccessDenied = true;
                ErrorMessage = "Accès refusé : cette page est réservée aux employés connectés.";
                return;
            }

            // Initialiser l'employeeId global
            _employeeId = _odooClient.session.Current.UserId!.Value;

            await LoadLeaveTypesAsync();
            await LoadBlockedDatesAsync();
            await CheckPendingRequestsAsync();
        }

        private async Task OnDatesChangedAsync()
        {
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
            ValidationMessage = string.Empty;
            HasOverlap = false;

            await LoadLeaveTypesAsync();
            await CheckOverlapAsync();

            UpdateValidationMessage();
            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task CheckOverlapAsync()
        {
            try
            {
                HasOverlap = await _odooClient.HasOverlappingLeaveAsync(
                    SelectedRange!.StartDate!.Value,
                    SelectedRange!.EndDate!.Value
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error CheckOverlapAsync: {ex.Message}");
                HasOverlap = false;
            }
        }

        private void UpdateValidationMessage()
        {
            ValidationMessage = SelectedLeaveType?.RequiresAllocation == true && (SelectedLeaveType.Days ?? 0) <= 0
                ? "Ce type de congé nécessite une allocation, mais vous n'avez plus de jours disponibles."
                : HasOverlap ? "Les dates sélectionnées chevauchent un congé existant." : string.Empty;
        }

        private async Task LoadBlockedDatesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                // Vérifier la connectivité
                bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

                if (isOnline)
                {
                    try
                    {
                        List<Leave> leaves = await _odooClient.GetLeavesAsync("confirm", "validate", "validate1");

                        _blockedDatesSet.Clear();

                        // Préparer les données pour le cache
                        List<(DateTime date, int leaveId, string status)> blockedDatesForCache = [];

                        foreach (Leave leave in leaves)
                        {
                            DateTime start = leave.StartDate.Date;
                            DateTime end = leave.EndDate.Date;

                            foreach (DateTime day in ExpandRangeToDays(start, end))
                            {
                                _ = _blockedDatesSet.Add(day);
                                blockedDatesForCache.Add((day, leave.Id, leave.Status));
                            }
                        }

                        // Sauvegarder en cache
                        await _databaseService.SaveBlockedDatesAsync(_employeeId, blockedDatesForCache);

                        System.Diagnostics.Debug.WriteLine($"Dates bloquées récupérées du serveur et mises en cache ({_blockedDatesSet.Count} dates)");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erreur serveur, utilisation du cache pour les dates bloquées: {ex.Message}");
                        await LoadBlockedDatesFromCacheAsync();
                    }
                }
                else
                {
                    await LoadBlockedDatesFromCacheAsync();
                }

                SelectableDayPredicate = date => !_blockedDatesSet.Contains(date.Date);
                OnPropertyChanged(nameof(SelectableDayPredicate));
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les dates non disponibles pour les congés.";
                System.Diagnostics.Debug.WriteLine($"Error LoadBlockedDatesAsync: {ex.Message}");
                _blockedDatesSet.Clear();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadBlockedDatesFromCacheAsync()
        {
            HashSet<DateTime> cached = await _databaseService.GetBlockedDatesAsync(_employeeId);

            _blockedDatesSet.Clear();

            if (cached.Count > 0)
            {
                foreach (DateTime date in cached)
                {
                    _ = _blockedDatesSet.Add(date);
                }

                System.Diagnostics.Debug.WriteLine($"Dates bloquées chargées depuis le cache ({cached.Count} dates)");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Aucune date bloquée en cache");
            }
        }

        private async Task LoadLeaveTypesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                // Vérifier la connectivité
                bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

                if (isOnline)
                {
                    try
                    {
                        List<LeaveTypeItem> typesWithoutAllocation = await _odooClient.GetLeaveTypesAsync();

                        List<AllocationSummary> allocations = await _odooClient.GetAllocationsSummaryAsync();

                        // Types avec allocation - triés par jours restants décroissant
                        List<LeaveTypeItem> typesWithAllocation = allocations
                            .Select(a => new LeaveTypeItem(
                                Id: a.LeaveTypeId,
                                Name: $"{a.LeaveTypeName} ({a.TotalRemaining} restants sur {a.TotalAllocated})",
                                RequiresAllocation: true,
                                Days: a.TotalRemaining
                            ))
                            .OrderByDescending(t => t.Days ?? 0)
                            .ToList();

                        // Ne garder que les types avec allocation (les autres ne sont pas sélectionnables)
                        List<LeaveTypeItem> combinedTypes = typesWithAllocation;

                        // Sauvegarder les types combinés dans la base de données
                        await _databaseService.SaveLeaveTypesAsync(_employeeId, combinedTypes);

                        LeaveTypes = new ObservableCollection<LeaveTypeItem>(combinedTypes);

                        SelectedLeaveType ??= LeaveTypes.FirstOrDefault();

                        UpdateValidationMessage();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erreur serveur, utilisation du cache pour les types de congés: {ex.Message}");
                        await LoadLeaveTypesFromCacheAsync();
                    }
                }
                else
                {
                    await LoadLeaveTypesFromCacheAsync();
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.";
                System.Diagnostics.Debug.WriteLine($"Error LoadLeaveTypesAsync: {ex.Message}");
                LeaveTypes = [];
                SelectedLeaveType = null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadLeaveTypesFromCacheAsync()
        {
            List<LeaveTypeItem> cached = await _databaseService.GetLeaveTypesAsync(_employeeId);

            if (cached.Count > 0)
            {
                LeaveTypes = new ObservableCollection<LeaveTypeItem>(cached);
                SelectedLeaveType = LeaveTypes.FirstOrDefault();

                System.Diagnostics.Debug.WriteLine($"Types de congés chargés depuis le cache ({cached.Count} types)");
            }
            else
            {
                LeaveTypes = [];
                SelectedLeaveType = null;

                ErrorMessage = IsOffline ? "Connectez-vous à Internet une première fois pour charger les types de congé." : "Aucun type de congé disponible.";
            }
        }

        public async Task<(bool Success, int? CreatedId, string Message)> SubmitAsync()
        {
            try
            {
                IsBusy = true;

                // Si hors-ligne, ne pas appeler le serveur
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    PendingLeaveRequest pending = new()
                    {
                        LeaveTypeId = SelectedLeaveType!.Id,
                        StartDate = SelectedRange!.StartDate!.Value,
                        EndDate = SelectedRange!.EndDate!.Value,
                        Reason = Reason?.Trim() ?? string.Empty,
                        CreatedAt = DateTime.UtcNow,
                        SyncStatus = SyncStatus.Pending
                    };

                    await _offlineService.AddPendingAsync(pending);

                    SyncMessage = "⏳ Demande enregistrée hors-ligne. Elle sera synchronisée automatiquement.";

                    return (true, null,
                        "📴 Vous êtes hors-ligne\n\nVotre demande a été enregistrée localement et sera envoyée automatiquement dès que la connexion reviendra.");
                }

                int createdId = await _odooClient.CreateLeaveRequestAsync(
                    leaveTypeId: SelectedLeaveType!.Id,
                    startDate: SelectedRange!.StartDate!.Value,
                    endDate: SelectedRange!.EndDate!.Value,
                    reason: Reason?.Trim() ?? string.Empty
                );

                return (true, createdId, $"✓ Demande envoyée avec succès\n\nVotre demande de congé a été créée dans Odoo (ID: {createdId}).");
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException ||
                                       Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                PendingLeaveRequest pending = new()
                {
                    LeaveTypeId = SelectedLeaveType!.Id,
                    StartDate = SelectedRange!.StartDate!.Value,
                    EndDate = SelectedRange!.EndDate!.Value,
                    Reason = Reason?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    SyncStatus = SyncStatus.Pending
                };

                try
                {
                    await _offlineService.AddPendingAsync(pending);

                    SyncMessage = "⏳ Demande enregistrée hors-ligne. Elle sera synchronisée automatiquement.";

                    return (true, null,
                        "⚠ Problème de connexion\n\nVotre demande a été enregistrée localement et sera envoyée automatiquement.");
                }
                catch (Exception addEx)
                {
                    return (false, null,
                        "❌ Erreur d'enregistrement\n\nImpossible d'enregistrer la demande hors-ligne.\n\nDétails : " + addEx.Message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error SubmitAsync: {ex.Message}");
                return (false, null, "Impossible d'envoyer la demande de congé.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            IsOffline = e.NetworkAccess != NetworkAccess.Internet;

            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                // Connexion rétablie : rafraîchir toutes les données
                await LoadLeaveTypesAsync();
                await LoadBlockedDatesAsync();
                await CheckPendingRequestsAsync();
            }
            else
            {
                // Connexion perdue : charger depuis le cache
                await LoadLeaveTypesFromCacheAsync();
                await LoadBlockedDatesFromCacheAsync();
            }
        }

        private void OnSyncStatusChanged(object? sender, SyncStatusEventArgs e)
        {
            if (!e.IsComplete)
            {
                IsSyncing = true;
                ShowSyncStatus = false;
                SyncMessage = $"Synchronisation de {e.PendingCount} demande{(e.PendingCount > 1 ? "s" : "")} en attente...";
            }
            else
            {
                IsSyncing = false;

                if (e.SuccessCount > 0 && e.PendingCount == 0)
                {
                    SyncMessage = $"✓ {e.SuccessCount} demande{(e.SuccessCount > 1 ? "s synchronisées" : " synchronisée")} avec succès";
                    ShowSyncStatus = true;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        SyncMessage = string.Empty;
                        ShowSyncStatus = false;
                    });
                }
                else if (e.PendingCount > 0)
                {
                    SyncMessage = $"⚠ {e.PendingCount} demande{(e.PendingCount > 1 ? "s" : "")} en attente de synchronisation";
                    ShowSyncStatus = true;
                }
                else
                {
                    SyncMessage = string.Empty;
                    ShowSyncStatus = false;
                }
            }
        }

        public async Task CheckPendingRequestsAsync()
        {
            try
            {
                List<PendingLeaveRequest> pending = await _offlineService.GetAllPendingAsync();

                if (pending.Count > 0)
                {
                    IsSyncing = true;
                    SyncMessage = $"Synchronisation de {pending.Count} demande{(pending.Count > 1 ? "s" : "")} en attente...";

                    await Task.Delay(2000);

                    pending = await _offlineService.GetAllPendingAsync();

                    if (pending.Count == 0)
                    {
                        SyncMessage = "✓ Toutes les demandes ont été synchronisées";
                        await Task.Delay(3000);
                        SyncMessage = string.Empty;
                    }
                    else
                    {
                        SyncMessage = $"⚠ {pending.Count} demande{(pending.Count > 1 ? "s" : "")} en attente de synchronisation";
                    }

                    IsSyncing = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error CheckPendingRequestsAsync: {ex.Message}");
                IsSyncing = false;
            }
        }
    }
}