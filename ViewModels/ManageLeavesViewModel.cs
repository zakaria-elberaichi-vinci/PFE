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
        private List<LeaveToApprove> _newLeaves = new();
        private int _pendingDecisionsCount;
        private bool _isOfflineMode = false;
        public ICommand RefreshOdooCommand { get; }

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

            Leaves = new ObservableCollection<LeaveToApprove>();

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
            _syncService.PendingCountChanged += (s, count) =>
            {
                PendingDecisionsCount = count;
            };

            // Recharger la liste après une synchronisation réussie
            _syncService.SyncCompleted += async (s, e) =>
            {
                await LoadAsync();
            };
            
            RefreshOdooCommand = new RelayCommand(
                    async _ => await RefreshLeavesFromOdooAsync(),
                    _ => !IsBusy
            );
        }

        public bool IsManager => _odoo.session.Current.IsManager;

        private int ManagerUserId => _odoo.session.Current.UserId ?? 0;

        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            private set
            {
                if (_isOfflineMode == value) return;
                _isOfflineMode = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<LeaveToApprove> Leaves { get; }

        /// <summary>
        /// Liste des nouvelles demandes détectées (non encore vues)
        /// </summary>
        public List<LeaveToApprove> NewLeaves => _newLeaves;

        /// <summary>
        /// Indique s'il y a de nouvelles demandes
        /// </summary>
        public bool HasNewLeaves => _newLeaves.Count > 0;

        /// <summary>
        /// Nombre de décisions en attente de synchronisation
        /// </summary>
        public int PendingDecisionsCount
        {
            get => _pendingDecisionsCount;
            private set
            {
                if (_pendingDecisionsCount == value) return;
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
                if (_isBusy == value) return;
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
                if (_errorMessage == value) return;
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        public string SuccessMessage
        {
            get => _successMessage;
            private set
            {
                if (_successMessage == value) return;
                _successMessage = value;
                OnPropertyChanged();
            }
        }

        public string InfoMessage
        {
            get => _infoMessage;
            private set
            {
                if (_infoMessage == value) return;
                _infoMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RefuseCommand { get; }

        public event EventHandler<string>? NotificationRequested;

        public async Task LoadAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;
            InfoMessage = string.Empty;
            _newLeaves.Clear();

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
                int pendingCount = allDecisions.Count(d => d.SyncStatus == SyncStatus.Pending || d.SyncStatus == SyncStatus.Failed);
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
                catch (Exception ex) when (IsNetworkError(ex))
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
                    _newLeaves = list.Where(l => !seenIds.Contains(l.Id)).ToList();
                    await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));
                }

                Leaves.Clear();
                foreach (LeaveToApprove item in list)
                    Leaves.Add(item);

                if (Leaves.Count == 0 && PendingDecisionsCount == 0)
                    ErrorMessage = "Aucune demande de congé en attente.";

                if (HasPendingDecisions && IsOfflineMode)
                    InfoMessage = $"Mode hors ligne. {PendingDecisionsCount} décision(s) en attente de synchronisation.";

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
            if (leave == null || IsBusy) return;

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
                
                Leaves.Remove(leave);
            }
            catch (Exception ex)
            {
                if (IsNetworkError(ex))
                {
                    // Sauvegarder localement
                    await SaveDecisionAsync(leave, "approve", SyncStatus.Pending);
                    await _databaseService.RemoveFromCacheAsync(leave.Id);
                    SuccessMessage = "Décision enregistrée. Elle sera synchronisée dès que la connexion sera rétablie.";
                    NotificationRequested?.Invoke(this, SuccessMessage);
                    IsOfflineMode = true;
                    Leaves.Remove(leave);
                    PendingDecisionsCount++;
                }
                else
                {
                    ErrorMessage = $"Impossible de valider la demande : {ex.Message}";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefuseAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy) return;

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
                
                Leaves.Remove(leave);
            }
            catch (Exception ex)
            {
                if (IsNetworkError(ex))
                {
                    // Sauvegarder localement
                    await SaveDecisionAsync(leave, "refuse", SyncStatus.Pending);
                    await _databaseService.RemoveFromCacheAsync(leave.Id);
                    SuccessMessage = "Décision enregistrée. Elle sera synchronisée dès que la connexion sera rétablie.";
                    NotificationRequested?.Invoke(this, SuccessMessage);
                    IsOfflineMode = true;
                    Leaves.Remove(leave);
                    PendingDecisionsCount++;
                }
                else
                {
                    ErrorMessage = $"Impossible de refuser la demande : {ex.Message}";
                }
            }
            finally
            {
                IsBusy = false;
            }
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

            await _databaseService.AddPendingLeaveDecisionAsync(decision);
            
            if (status == SyncStatus.Synced)
            {
                await _databaseService.UpdateDecisionSyncStatusAsync(decision.Id, SyncStatus.Synced);
            }
        }

        private static bool IsNetworkError(Exception ex)
        {
            return ex is HttpRequestException ||
                   ex.InnerException is HttpRequestException ||
                   ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("host", StringComparison.OrdinalIgnoreCase);
        }

        private void RaiseAllCanExecuteChanged()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApproveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefuseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public async Task RefreshLeavesFromOdooAsync()
        {
            if (IsBusy) return;

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
                foreach (var leave in leaves)
                    Leaves.Add(leave);

                if (Leaves.Count == 0)
                    ErrorMessage = "Aucune demande de congé en attente.";
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
    }
}
