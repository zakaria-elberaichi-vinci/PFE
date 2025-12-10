using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Networking;
using PFE.Helpers;
using PFE.Models;
using PFE.Services;
using Syncfusion.Maui.Calendar;
using static PFE.Helpers.DateHelper;
using DB = PFE.Models.Database;

namespace PFE.ViewModels
{
    public class LeaveRequestViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;
        private readonly IDatabaseService _databaseService;

        private CalendarDateRange? _selectedRange;

        private ObservableCollection<LeaveTypeItem> _leaveTypes = new();

        private HashSet<DateTime> _blockedDatesSet = new();

        private LeaveTypeItem? _selectedLeaveType;

        private bool _useAllocation;
        private string _reason = string.Empty;
        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _validationMessage = string.Empty;
        private bool _isAccessDenied;
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

            RefreshCommand = new RelayCommand(
                async _ => await RefreshTotalsAsync(),
                _ => !IsBusy && AreDatesValid && IsEmployee
            );

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
                    OnPropertyChanged(nameof(LeaveTypes));
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
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool UseAllocation
        {
            get => _useAllocation;
            set
            {
                if (Set(ref _useAllocation, value))
                {
                    OnPropertyChanged(nameof(UseAllocation));
                    ValidationMessage = string.Empty;
                    _ = OnUseAllocationChangedAsync();
                }
            }
        }

        public string Reason
        {
            get => _reason;
            set
            {
                if (Set(ref _reason, value))
                    OnPropertyChanged(nameof(Reason));
            }
        }

        public int TotalAllocated
        {
            get => _totalAllocated;
            private set
            {
                if (Set(ref _totalAllocated, value))
                    OnPropertyChanged(nameof(TotalAllocated));
            }
        }

        public int TotalTaken
        {
            get => _totalTaken;
            private set
            {
                if (Set(ref _totalTaken, value))
                    OnPropertyChanged(nameof(TotalTaken));
            }
        }

        public int TotalRemaining
        {
            get => _totalRemaining;
            private set
            {
                if (Set(ref _totalRemaining, value))
                {
                    OnPropertyChanged(nameof(TotalRemaining));
                    OnPropertyChanged(nameof(CanSubmit));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    OnPropertyChanged(nameof(ErrorMessage));
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (Set(ref _validationMessage, value))
                    OnPropertyChanged(nameof(ValidationMessage));
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
                    OnPropertyChanged(nameof(IsAccessDenied));
            }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            private set
            {
                if (Set(ref _isSyncing, value))
                    OnPropertyChanged(nameof(IsSyncing));
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
                    OnPropertyChanged(nameof(ShowSyncStatus));
            }
        }

        public bool IsOffline
        {
            get => _isOffline;
            private set
            {
                if (Set(ref _isOffline, value))
                    OnPropertyChanged(nameof(IsOffline));
            }
        }

        public bool AreDatesValid => SelectedRange?.StartDate != null && SelectedRange?.EndDate != null;

        public bool CanSubmit => !IsBusy && AreDatesValid && SelectedLeaveType is not null && !(UseAllocation && TotalRemaining == 0);

        public ICommand RefreshCommand { get; }
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

            await LoadLeaveTypesAsync();
            await RefreshTotalsAsync();
            await LoadBlockedDatesAsync();
            await CheckPendingRequestsAsync();
        }

        private async Task OnUseAllocationChangedAsync()
        {
            // Mettre à jour l'état hors-ligne
            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
            
            await LoadLeaveTypesAsync();

            if (UseAllocation && TotalRemaining == 0)
            {
                ValidationMessage = "Votre solde d'allocation est de 0.";
            }

            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task OnDatesChangedAsync()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();

            ValidationMessage = string.Empty;

            await RefreshTotalsAsync();

            if (UseAllocation)
            {
                await LoadLeaveTypesAsync();
                if (TotalRemaining == 0)
                {
                    ValidationMessage = "Votre solde d'allocation est de 0.";
                    return;
                }
            }

            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task LoadBlockedDatesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                // Vérifier la connectivité
                bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
                int employeeId = _odooClient.session.Current.UserId ?? 0;

                if (isOnline)
                {
                    try
                    {
                        // Essayer de récupérer depuis le serveur
                        List<Leave> leaves = await _odooClient.GetLeavesAsync(
                            null, "confirm", "validate", "validate1", "refuse");

                        _blockedDatesSet.Clear();

                        // Préparer les données pour le cache
                        List<(DateTime date, int leaveId, string status)> blockedDatesForCache = new();

                        foreach (Leave leave in leaves)
                        {
                            DateTime start = leave.StartDate.Date;
                            DateTime end = leave.EndDate.Date;

                            foreach (DateTime day in ExpandRangeToDays(start, end))
                            {
                                _blockedDatesSet.Add(day);
                                blockedDatesForCache.Add((day, leave.Id, leave.Status));
                            }
                        }

                        // Sauvegarder en cache
                        await _databaseService.SaveBlockedDatesAsync(employeeId, blockedDatesForCache);

                        string format = "dd/MM/yyyy";
                        System.Diagnostics.Debug.WriteLine($"RES blockedDates: {string.Join(',', _blockedDatesSet.Select(d => d.ToString(format)))}");
                        System.Diagnostics.Debug.WriteLine($"Dates bloquées récupérées du serveur et mises en cache ({_blockedDatesSet.Count} dates)");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erreur serveur, utilisation du cache pour les dates bloquées: {ex.Message}");
                        await LoadBlockedDatesFromCacheAsync(employeeId);
                    }
                }
                else
                {
                    // Mode hors-ligne : charger depuis le cache
                    await LoadBlockedDatesFromCacheAsync(employeeId);
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

        private async Task LoadBlockedDatesFromCacheAsync(int employeeId)
        {
            HashSet<DateTime> cached = await _databaseService.GetBlockedDatesAsync(employeeId);

            _blockedDatesSet.Clear();

            if (cached.Count > 0)
            {
                foreach (var date in cached)
                {
                    _blockedDatesSet.Add(date);
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
                int employeeId = _odooClient.session.Current.UserId ?? 0;
                int? year = SelectedRange?.StartDate?.Year;

                if (isOnline)
                {
                    try
                    {
                        // Toujours charger et sauvegarder les types SANS allocation pour le mode hors-ligne
                        List<LeaveTypeItem> baseTypes = await _odooClient.GetLeaveTypesAsync(false, year);
                        List<LeaveTypeItem> baseTypesForCache = baseTypes.Select(lt => 
                            new LeaveTypeItem(lt.Id, lt.Name, false)).ToList();
                        await _databaseService.SaveLeaveTypesAsync(employeeId, baseTypesForCache, year, false);
                        System.Diagnostics.Debug.WriteLine($"Types de base sauvegardés: {baseTypesForCache.Count}");

                        // Ensuite récupérer les types selon UseAllocation
                        List<LeaveTypeItem> filtered;
                        if (UseAllocation)
                        {
                            filtered = await _odooClient.GetLeaveTypesAsync(true, year);
                            
                            // Sauvegarder aussi les types avec allocation
                            List<LeaveTypeItem> allocTypesForCache = filtered.Select(lt => 
                                new LeaveTypeItem(lt.Id, lt.Name, true)).ToList();
                            await _databaseService.SaveLeaveTypesAsync(employeeId, allocTypesForCache, year, true);
                            
                            // Ajouter le nombre de jours restants pour l'affichage
                            List<LeaveTypeItem> updatedList = new();
                            foreach (LeaveTypeItem leave in filtered)
                            {
                                (_, _, int remaining) = await _odooClient.GetNumberTimeOffAsync(
                                    yearRequested: year,
                                    idRequest: leave.Id);

                                updatedList.Add(leave with { Name = $"{leave.Name} ({remaining} restants)" });
                            }
                            filtered = updatedList;
                        }
                        else
                        {
                            filtered = baseTypes;
                        }

                        LeaveTypes = new ObservableCollection<LeaveTypeItem>(filtered);
                        SelectedLeaveType = LeaveTypes.FirstOrDefault();

                        if (LeaveTypes.Count == 0)
                        {
                            ErrorMessage = "Aucun type de congé disponible. Veuillez demander une allocation.";
                        }

                        System.Diagnostics.Debug.WriteLine($"Types de congés récupérés du serveur et mis en cache ({filtered.Count} types)");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erreur serveur, utilisation du cache pour les types: {ex.Message}");
                        await LoadLeaveTypesFromCacheAsync(employeeId, year, UseAllocation);
                    }
                }
                else
                {
                    // Mode hors-ligne : charger depuis le cache
                    await LoadLeaveTypesFromCacheAsync(employeeId, year, UseAllocation);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.";
                System.Diagnostics.Debug.WriteLine($"Error LoadLeaveTypesAsync: {ex.Message}");
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadLeaveTypesFromCacheAsync(int employeeId, int? year, bool requiresAllocation)
        {
            var cached = await _databaseService.GetLeaveTypesAsync(employeeId, year, requiresAllocation);

            // Si rien trouvé avec requiresAllocation, essayer sans (types de base)
            if ((cached == null || cached.Count == 0) && requiresAllocation)
            {
                cached = await _databaseService.GetLeaveTypesAsync(employeeId, year, false);
                System.Diagnostics.Debug.WriteLine($"Fallback: types de congés sans allocation chargés depuis le cache");
            }

            // Si toujours rien, essayer sans année spécifique
            if ((cached == null || cached.Count == 0) && year.HasValue)
            {
                cached = await _databaseService.GetLeaveTypesAsync(employeeId, null, requiresAllocation);
                
                if ((cached == null || cached.Count == 0) && requiresAllocation)
                {
                    cached = await _databaseService.GetLeaveTypesAsync(employeeId, null, false);
                }
                
                System.Diagnostics.Debug.WriteLine($"Fallback: types de congés sans année chargés depuis le cache");
            }

            if (cached != null && cached.Count > 0)
            {
                LeaveTypes = new ObservableCollection<LeaveTypeItem>(cached);
                SelectedLeaveType = LeaveTypes.FirstOrDefault();

                System.Diagnostics.Debug.WriteLine($"Types de congés chargés depuis le cache ({cached.Count} types)");
            }
            else
            {
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;

                if (IsOffline)
                {
                    ErrorMessage = "Connectez-vous à Internet une première fois pour charger les types de congé.";
                }
                else
                {
                    ErrorMessage = "Aucun type de congé disponible.";
                }
            }
        }

        public async Task RefreshTotalsAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                // Vérifier la connectivité
                bool isOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
                IsOffline = !isOnline;

                int year = SelectedRange?.StartDate?.Year ?? DateTime.Today.Year;
                int employeeId = _odooClient.session.Current.UserId ?? 0;

                if (isOnline)
                {
                    try
                    {
                        // Essayer de récupérer depuis le serveur
                        (int allocated, int taken, int total) =
                            await _odooClient.GetNumberTimeOffAsync(SelectedRange?.StartDate?.Year);
                        
                        TotalAllocated = allocated;
                        TotalTaken = taken;
                        TotalRemaining = total;

                        // Sauvegarder en cache
                        await _databaseService.SaveLeaveAllocationAsync(employeeId, year, allocated, taken, total);
                        
                        System.Diagnostics.Debug.WriteLine($"Allocations récupérées du serveur et mises en cache");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erreur serveur, utilisation du cache: {ex.Message}");
                        await LoadFromCacheAsync(employeeId, year);
                    }
                }
                else
                {
                    // Mode hors-ligne : charger depuis le cache
                    await LoadFromCacheAsync(employeeId, year);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de rafraîchir les totaux de congés.";
                System.Diagnostics.Debug.WriteLine($"Error RefreshTotalsAsync: {ex.Message}");
                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadFromCacheAsync(int employeeId, int year)
        {
            DB.CachedLeaveAllocation? cached = await _databaseService.GetLeaveAllocationAsync(employeeId, year);
            
            if (cached is not null)
            {
                TotalAllocated = cached.Allocated;
                TotalTaken = cached.Taken;
                TotalRemaining = cached.Remaining;
                
                System.Diagnostics.Debug.WriteLine($"Allocations chargées depuis le cache (dernière mise à jour: {cached.LastUpdated})");
            }
            else
            {
                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
                
                if (IsOffline)
                {
                    ErrorMessage = "Aucune donnée en cache. Veuillez vous connecter à Internet.";
                }
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
                    var pending = new PendingLeaveRequest
                    {
                        Id = Guid.NewGuid(),
                        LeaveTypeId = SelectedLeaveType!.Id,
                        StartDate = SelectedRange!.StartDate!.Value,
                        EndDate = SelectedRange!.EndDate!.Value,
                        Reason = Reason?.Trim() ?? string.Empty,
                        QueuedAt = DateTime.UtcNow
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
                var pending = new PendingLeaveRequest
                {
                    Id = Guid.NewGuid(),
                    LeaveTypeId = SelectedLeaveType!.Id,
                    StartDate = SelectedRange!.StartDate!.Value,
                    EndDate = SelectedRange!.EndDate!.Value,
                    Reason = Reason?.Trim() ?? string.Empty,
                    QueuedAt = DateTime.UtcNow
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
                System.Diagnostics.Debug.WriteLine(
                    $"Res SubmitAsync: {SelectedRange!.StartDate!.Value} - {SelectedRange!.EndDate!.Value}");
                System.Diagnostics.Debug.WriteLine($"Error SubmitAsync: {ex.Message}");
                return (false, null, "❌ Erreur\n\nImpossible d'envoyer la demande de congé.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            IsOffline = e.NetworkAccess != NetworkAccess.Internet;
            
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                // Connexion rétablie : rafraîchir toutes les données
                await LoadLeaveTypesAsync();
                await RefreshTotalsAsync();
                await LoadBlockedDatesAsync();
                await CheckPendingRequestsAsync();
            }
            else
            {
                // Connexion perdue : charger depuis le cache
                int year = SelectedRange?.StartDate?.Year ?? DateTime.Today.Year;
                int employeeId = _odooClient.session.Current.UserId ?? 0;
                await LoadFromCacheAsync(employeeId, year);
                await LoadLeaveTypesFromCacheAsync(employeeId, year, UseAllocation);
                await LoadBlockedDatesFromCacheAsync(employeeId);
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
