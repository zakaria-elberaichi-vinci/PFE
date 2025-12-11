using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Models.Database;
using PFE.Services;

namespace PFE.ViewModels
{
    public class ManageLeavesViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odoo;
        private readonly ILeaveNotificationService _notificationService;
        private readonly IDatabaseService _databaseService;
        private readonly ISyncService _syncService;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _successMessage = string.Empty;
        private string _infoMessage = string.Empty;
        private int _pendingDecisionsCount;
        private bool _isOfflineMode = false;
        private bool _isReauthenticating = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ManageLeavesViewModel(
            OdooClient odoo,
            ILeaveNotificationService notificationService,
            IDatabaseService databaseService,
            ISyncService syncService)
        {
            _odoo = odoo;
            _notificationService = notificationService;
            _databaseService = databaseService;
            _syncService = syncService;

            Leaves = [];

            RefreshCommand = new RelayCommand(
                async _ => await LoadAsync(),
                _ => !IsBusy
            );

            ApproveCommand = new RelayCommand(
                async param => await ApproveAsync(param as LeaveToApprove),
                param => !IsBusy && param is LeaveToApprove
            );

            RefuseCommand = new RelayCommand(
                async param => await RefuseAsync(param as LeaveToApprove),
                param => !IsBusy && param is LeaveToApprove
            );

            // Écouter les changements du compteur de décisions en attente
            _syncService.PendingCountChanged += OnPendingCountChanged;

            // Recharger la liste après une synchronisation réussie
            _syncService.SyncCompleted += OnSyncCompleted;

            RefreshOdooCommand = new RelayCommand(
                    async _ => await RefreshLeavesFromOdooAsync(),
                    _ => !IsBusy
            );

            ClearCacheCommand = new RelayCommand(
                    async _ => await ClearCacheAsync(),
                    _ => !IsBusy
            );
        }

        private void OnPendingCountChanged(object? sender, int count)
        {
            // Mettre à jour sur le thread principal
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PendingDecisionsCount = count;
                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: PendingCountChanged -> {count}");
                
                // Si plus de décisions en attente et qu'on était en mode offline, afficher un message de succès
                if (count == 0 && IsOfflineMode)
                {
                    SuccessMessage = "✓ Toutes les décisions ont été synchronisées !";
                    IsOfflineMode = false;
                    
                    // Effacer le message après quelques secondes
                    _ = ClearSuccessMessageAfterDelayAsync();
                }
            });
        }

        private async void OnSyncCompleted(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ManageLeavesViewModel: SyncCompleted reçu");
            
            // Recharger les données sur le thread principal
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Recalculer le compteur depuis la DB
                await RefreshPendingCountAsync();
                
                // Recharger la liste
                await LoadAsync();
            });
        }

        private async Task RefreshPendingCountAsync()
        {
            try
            {
                List<PendingLeaveDecision> pendingDecisions = await _databaseService.GetUnsyncedLeaveDecisionsAsync();
                int newCount = pendingDecisions.Count;
                
                if (PendingDecisionsCount != newCount)
                {
                    PendingDecisionsCount = newCount;
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: RefreshPendingCount -> {newCount}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Erreur RefreshPendingCount - {ex.Message}");
            }
        }

        private async Task ClearSuccessMessageAfterDelayAsync()
        {
            await Task.Delay(5000);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (SuccessMessage.Contains("synchronisées"))
                {
                    SuccessMessage = string.Empty;
                }
            });
        }

        public bool IsManager => _odoo.session.Current.IsManager;

        private int ManagerUserId => _odoo.session.Current.UserId ?? 0;

        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            private set
            {
                if (_isOfflineMode == value)
                {
                    return;
                }

                _isOfflineMode = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<LeaveToApprove> Leaves { get; }

        /// <summary>
        /// Liste des nouvelles demandes détectées (non encore vues)
        /// </summary>
        public List<LeaveToApprove> NewLeaves { get; private set; } = [];

        /// <summary>
        /// Indique s'il y a de nouvelles demandes
        /// </summary>
        public bool HasNewLeaves => NewLeaves.Count > 0;

        /// <summary>
        /// Nombre de décisions en attente de synchronisation
        /// </summary>
        public int PendingDecisionsCount
        {
            get => _pendingDecisionsCount;
            private set
            {
                if (_pendingDecisionsCount == value)
                {
                    return;
                }

                _pendingDecisionsCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPendingDecisions));
            }
        }

        /// <summary>
        /// Indique s'il y a des décisions en attente de sync
        /// </summary>
        public bool HasPendingDecisions => PendingDecisionsCount > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value)
                {
                    return;
                }

                _isBusy = value;
                OnPropertyChanged();
                RaiseAllCanExecuteChanged();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (_errorMessage == value)
                {
                    return;
                }

                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        public string SuccessMessage
        {
            get => _successMessage;
            private set
            {
                if (_successMessage == value)
                {
                    return;
                }

                _successMessage = value;
                OnPropertyChanged();
            }
        }

        public string InfoMessage
        {
            get => _infoMessage;
            private set
            {
                if (_infoMessage == value)
                {
                    return;
                }

                _infoMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RefuseCommand { get; }
        public ICommand RefreshOdooCommand { get; }
        public ICommand ClearCacheCommand { get; }

        public event EventHandler<string>? NotificationRequested;

        public async Task LoadAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;
            InfoMessage = string.Empty;
            NewLeaves.Clear();

            try
            {
                if (!IsManager)
                {
                    ErrorMessage = "Veuillez vous connecter en tant que manager.";
                    Leaves.Clear();
                    return;
                }

                // Récupérer toutes les décisions déjà prises
                List<PendingLeaveDecision> allDecisions = await _databaseService.GetAllLeaveDecisionsAsync(ManagerUserId);
                HashSet<int> processedLeaveIds = allDecisions.Select(d => d.LeaveId).ToHashSet();

                // Mettre à jour le compteur de décisions en attente
                int pendingCount = allDecisions.Count(d => d.SyncStatus is SyncStatus.Pending or SyncStatus.Failed);
                PendingDecisionsCount = pendingCount;

                // Essayer de charger les demandes depuis Odoo
                List<LeaveToApprove> list;
                try
                {
                    list = await _odoo.GetLeavesToApproveAsync();
                    IsOfflineMode = false;

                    // Mettre à jour le cache avec les données fraîches
                    await UpdateCacheAsync(list);
                }
                catch (Exception ex) when (IsSessionExpiredError(ex))
                {
                    // Session expirée - tenter une ré-authentification
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Session expirée, tentative de ré-authentification...");

                    bool reauthSuccess = await TryReauthenticateAsync();

                    if (reauthSuccess)
                    {
                        // Réessayer le chargement après ré-authentification
                        try
                        {
                            list = await _odoo.GetLeavesToApproveAsync();
                            IsOfflineMode = false;
                            await UpdateCacheAsync(list);
                            InfoMessage = "Session restaurée automatiquement.";
                        }
                        catch (Exception retryEx)
                        {
                            // Échec après réauth - passer en mode offline
                            System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Échec après réauth - {retryEx.Message}");
                            IsOfflineMode = true;
                            InfoMessage = "Mode hors ligne. Affichage des données en cache.";
                            list = await LoadFromCacheAsync();
                        }
                    }
                    else
                    {
                        // Réauthentification échouée - passer en mode offline
                        IsOfflineMode = true;
                        InfoMessage = "Session expirée. Affichage des données en cache.";
                        list = await LoadFromCacheAsync();
                    }
                }
                catch (Exception ex) when (IsNetworkOrOfflineError(ex))
                {
                    // Mode hors ligne : charger depuis le cache
                    IsOfflineMode = true;
                    InfoMessage = "Mode hors ligne. Affichage des données en cache.";
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Mode offline - {ex.Message}");

                    list = await LoadFromCacheAsync();
                }

                // Exclure les congés déjà traités
                list = list.Where(l => !processedLeaveIds.Contains(l.Id)).ToList();

                // Détecter les nouvelles demandes (seulement en mode online)
                if (!IsOfflineMode)
                {
                    HashSet<int> seenIds = await _notificationService.GetSeenLeaveIdsAsync(ManagerUserId);
                    NewLeaves = list.Where(l => !seenIds.Contains(l.Id)).ToList();
                    await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));
                }

                Leaves.Clear();
                foreach (LeaveToApprove item in list)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0 && PendingDecisionsCount == 0)
                {
                    ErrorMessage = "Aucune demande de congé en attente.";
                }

                if (HasPendingDecisions && IsOfflineMode)
                {
                    InfoMessage = $"Mode hors ligne. {PendingDecisionsCount} décision(s) en attente de synchronisation.";
                }

                OnPropertyChanged(nameof(NewLeaves));
                OnPropertyChanged(nameof(HasNewLeaves));
                OnPropertyChanged(nameof(IsOfflineMode));

                // Nettoyer les anciennes décisions
                await _databaseService.CleanupOldSyncedDecisionsAsync(30);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur lors du chargement : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsManager));
            }
        }

        private async Task UpdateCacheAsync(List<LeaveToApprove> leaves)
        {
            List<CachedLeaveToApprove> cached = leaves.Select(l => new CachedLeaveToApprove
            {
                LeaveId = l.Id,
                ManagerUserId = ManagerUserId,
                EmployeeName = l.EmployeeName,
                LeaveType = l.Type,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                Days = l.Days,
                Status = l.Status,
                Reason = l.Reason,
                CanValidate = l.CanValidate,
                CanRefuse = l.CanRefuse
            }).ToList();

            await _databaseService.UpdateLeavesToApproveCacheAsync(ManagerUserId, cached);
        }

        private async Task<List<LeaveToApprove>> LoadFromCacheAsync()
        {
            List<CachedLeaveToApprove> cached = await _databaseService.GetCachedLeavesToApproveAsync(ManagerUserId);

            return cached.Select(c => new LeaveToApprove(
                c.LeaveId,
                c.EmployeeName,
                c.LeaveType,
                c.StartDate,
                c.EndDate,
                c.Days,
                c.Status,
                c.Reason,
                c.CanValidate,
                c.CanRefuse
            )).ToList();
        }

        private async Task ApproveAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy)
            {
                return;
            }

            bool alreadyProcessed = await _databaseService.HasDecisionForLeaveAsync(leave.Id);
            if (alreadyProcessed)
            {
                ErrorMessage = "Une décision a déjà été prise pour cette demande.";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                // Essayer d'envoyer à Odoo
                await _odoo.ApproveLeaveAsync(leave.Id);

                // Succès - sauvegarder comme synchronisé
                await SaveDecisionAsync(leave, "approve", SyncStatus.Synced);
                await _databaseService.RemoveFromCacheAsync(leave.Id);
                SuccessMessage = "La demande de congé a été validée avec succès !";
                NotificationRequested?.Invoke(this, SuccessMessage);
                IsOfflineMode = false;

                _ = Leaves.Remove(leave);
            }
            catch (Exception ex) when (IsSessionExpiredError(ex))
            {
                // Session expirée - tenter réauth puis réessayer
                bool reauthSuccess = await TryReauthenticateAsync();

                if (reauthSuccess)
                {
                    try
                    {
                        await _odoo.ApproveLeaveAsync(leave.Id);
                        await SaveDecisionAsync(leave, "approve", SyncStatus.Synced);
                        await _databaseService.RemoveFromCacheAsync(leave.Id);
                        SuccessMessage = "La demande de congé a été validée avec succès !";
                        NotificationRequested?.Invoke(this, SuccessMessage);
                        IsOfflineMode = false;
                        _ = Leaves.Remove(leave);
                    }
                    catch
                    {
                        // Sauvegarder localement
                        await SaveDecisionLocally(leave, "approve");
                    }
                }
                else
                {
                    // Sauvegarder localement
                    await SaveDecisionLocally(leave, "approve");
                }
            }
            catch (Exception ex) when (IsNetworkOrOfflineError(ex))
            {
                // Sauvegarder localement
                await SaveDecisionLocally(leave, "approve");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de valider la demande : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefuseAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy)
            {
                return;
            }

            bool alreadyProcessed = await _databaseService.HasDecisionForLeaveAsync(leave.Id);
            if (alreadyProcessed)
            {
                ErrorMessage = "Une décision a déjà été prise pour cette demande.";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                // Essayer d'envoyer à Odoo
                await _odoo.RefuseLeaveAsync(leave.Id);

                // Succès - sauvegarder comme synchronisé
                await SaveDecisionAsync(leave, "refuse", SyncStatus.Synced);
                await _databaseService.RemoveFromCacheAsync(leave.Id);
                SuccessMessage = "La demande de congé a été refusée avec succès !";
                NotificationRequested?.Invoke(this, SuccessMessage);
                IsOfflineMode = false;

                _ = Leaves.Remove(leave);
            }
            catch (Exception ex) when (IsSessionExpiredError(ex))
            {
                // Session expirée - tenter réauth puis réessayer
                bool reauthSuccess = await TryReauthenticateAsync();

                if (reauthSuccess)
                {
                    try
                    {
                        await _odoo.RefuseLeaveAsync(leave.Id);
                        await SaveDecisionAsync(leave, "refuse", SyncStatus.Synced);
                        await _databaseService.RemoveFromCacheAsync(leave.Id);
                        SuccessMessage = "La demande de congé a été refusée avec succès !";
                        NotificationRequested?.Invoke(this, SuccessMessage);
                        IsOfflineMode = false;
                        _ = Leaves.Remove(leave);
                    }
                    catch
                    {
                        // Sauvegarder localement
                        await SaveDecisionLocally(leave, "refuse");
                    }
                }
                else
                {
                    // Sauvegarder localement
                    await SaveDecisionLocally(leave, "refuse");
                }
            }
            catch (Exception ex) when (IsNetworkOrOfflineError(ex))
            {
                // Sauvegarder localement
                await SaveDecisionLocally(leave, "refuse");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de refuser la demande : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SaveDecisionLocally(LeaveToApprove leave, string decisionType)
        {
            await SaveDecisionAsync(leave, decisionType, SyncStatus.Pending);
            await _databaseService.RemoveFromCacheAsync(leave.Id);
            SuccessMessage = "Décision enregistrée. Elle sera synchronisée dès que la connexion sera rétablie.";
            NotificationRequested?.Invoke(this, SuccessMessage);
            IsOfflineMode = true;
            _ = Leaves.Remove(leave);
            PendingDecisionsCount++;
        }

        private async Task SaveDecisionAsync(LeaveToApprove leave, string decisionType, SyncStatus status)
        {
            PendingLeaveDecision decision = new()
            {
                ManagerUserId = ManagerUserId,
                LeaveId = leave.Id,
                DecisionType = decisionType,
                EmployeeName = leave.EmployeeName,
                LeaveStartDate = leave.StartDate,
                LeaveEndDate = leave.EndDate,
                SyncStatus = status
            };

            _ = await _databaseService.AddPendingLeaveDecisionAsync(decision);

            if (status == SyncStatus.Synced)
            {
                await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
            }
        }

        /// <summary>
        /// Vérifie si l'erreur est une erreur de session expirée Odoo
        /// </summary>
        private static bool IsSessionExpiredError(Exception ex)
        {
            string message = ex.Message.ToLowerInvariant();
            return message.Contains("session expired") ||
                   message.Contains("sessionexpired") ||
                   message.Contains("session_expired") ||
                   message.Contains("odoo session expired");
        }

        /// <summary>
        /// Vérifie si l'erreur est une erreur réseau ou de connexion
        /// </summary>
        private static bool IsNetworkOrOfflineError(Exception ex)
        {
            return ex is HttpRequestException ||
                   ex.InnerException is HttpRequestException ||
                   ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("host", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tente de se ré-authentifier avec les credentials sauvegardés
        /// </summary>
        private async Task<bool> TryReauthenticateAsync()
        {
            if (_isReauthenticating)
            {
                System.Diagnostics.Debug.WriteLine("ManageLeavesViewModel: Ré-authentification déjà en cours");
                return false;
            }

            _isReauthenticating = true;

            try
            {
                // Récupérer les credentials sauvegardés
                string login = Preferences.Get("auth.login", string.Empty);
                string? password = null;

                try
                {
                    password = await SecureStorage.GetAsync("auth.password");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Erreur lecture SecureStorage - {ex.Message}");
                }

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    System.Diagnostics.Debug.WriteLine("ManageLeavesViewModel: Credentials non disponibles pour la ré-authentification");
                    return false;
                }

                // Tenter la connexion
                bool success = await _odoo.LoginAsync(login, password);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Ré-authentification réussie pour {login}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Ré-authentification échouée pour {login}");
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Exception lors de la ré-authentification - {ex.Message}");
                return false;
            }
            finally
            {
                _isReauthenticating = false;
            }
        }

        private void RaiseAllCanExecuteChanged()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApproveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefuseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task RefreshLeavesFromOdooAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                if (!IsManager)
                {
                    ErrorMessage = "Veuillez vous connecter en tant que manager.";
                    return;
                }

                // Appel direct à l'API Odoo
                List<LeaveToApprove> leaves = await _odoo.GetLeavesToApproveAsync();

                // Mettre à jour la collection affichée
                Leaves.Clear();
                foreach (LeaveToApprove leave in leaves)
                {
                    Leaves.Add(leave);
                }

                if (Leaves.Count == 0)
                {
                    ErrorMessage = "Aucune demande de congé en attente.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur lors de l'actualisation : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Vide le cache local (décisions en attente et demandes en cache)
        /// </summary>
        private async Task ClearCacheAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                // Vider le cache des demandes à approuver
                await _databaseService.ClearLeavesToApproveCacheAsync(ManagerUserId);

                // Supprimer TOUTES les décisions (y compris Pending et Failed)
                await ClearAllPendingDecisionsAsync();

                // Rafraîchir le compteur
                PendingDecisionsCount = 0;
                OnPropertyChanged(nameof(HasPendingDecisions));

                SuccessMessage = "✓ Cache vidé avec succès !";
                System.Diagnostics.Debug.WriteLine("ManageLeavesViewModel: Cache vidé");

                // Recharger les données
                await LoadAsync();

                // Effacer le message après quelques secondes
                _ = ClearSuccessMessageAfterDelayAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur lors du vidage du cache : {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Erreur ClearCache - {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Supprime toutes les décisions en attente pour ce manager
        /// </summary>
        private async Task ClearAllPendingDecisionsAsync()
        {
            try
            {
                // Récupérer toutes les décisions de ce manager
                List<PendingLeaveDecision> allDecisions = await _databaseService.GetAllLeaveDecisionsAsync(ManagerUserId);
                
                // Supprimer chaque décision
                foreach (var decision in allDecisions)
                {
                    await _databaseService.DeletePendingLeaveDecisionAsync(decision.Id);
                    System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Décision {decision.Id} supprimée");
                }

                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: {allDecisions.Count} décision(s) supprimée(s)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ManageLeavesViewModel: Erreur ClearAllPendingDecisions - {ex.Message}");
                throw;
            }
        }
    }
}