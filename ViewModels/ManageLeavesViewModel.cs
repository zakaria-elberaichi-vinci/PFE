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
        private List<LeaveToApprove> _newLeaves = new();
        private int _pendingDecisionsCount;

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
        }

        public bool IsManager => _odoo.session.Current.IsManager;

        private int ManagerUserId => _odoo.session.Current.UserId ?? 0;

        public bool IsOnline => _syncService.IsOnline;

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
            _newLeaves.Clear();

            try
            {
                if (!IsManager)
                {
                    ErrorMessage = "Veuillez vous connecter en tant que manager.";
                    Leaves.Clear();
                    return;
                }

                // Mettre à jour le compteur de décisions en attente
                List<PendingLeaveDecision> pendingDecisions = await _databaseService.GetPendingLeaveDecisionsAsync(ManagerUserId);
                PendingDecisionsCount = pendingDecisions.Count;

                if (!IsOnline)
                {
                    ErrorMessage = "Mode hors ligne. Les décisions seront synchronisées dès que la connexion sera rétablie.";
                    // En mode offline, on ne peut pas charger les nouvelles demandes
                    return;
                }

                List<LeaveToApprove> list = await _odoo.GetLeavesToApproveAsync();

                // Exclure les congés pour lesquels une décision est déjà en attente
                HashSet<int> pendingLeaveIds = pendingDecisions.Select(d => d.LeaveId).ToHashSet();
                list = list.Where(l => !pendingLeaveIds.Contains(l.Id)).ToList();

                // Détecter les nouvelles demandes
                HashSet<int> seenIds = await _notificationService.GetSeenLeaveIdsAsync(ManagerUserId);
                _newLeaves = list.Where(l => !seenIds.Contains(l.Id)).ToList();

                // Marquer toutes les demandes actuelles comme vues
                await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));

                Leaves.Clear();
                foreach (LeaveToApprove item in list)
                    Leaves.Add(item);

                if (Leaves.Count == 0 && PendingDecisionsCount == 0)
                    ErrorMessage = "Aucune demande de congé en attente.";

                OnPropertyChanged(nameof(NewLeaves));
                OnPropertyChanged(nameof(HasNewLeaves));
                OnPropertyChanged(nameof(IsOnline));
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

        private async Task ApproveAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                if (IsOnline)
                {
                    // Mode en ligne : envoyer directement à Odoo
                    await _odoo.ApproveLeaveAsync(leave.Id);
                    SuccessMessage = "La demande de congé a été validée avec succès !";
                    NotificationRequested?.Invoke(this, SuccessMessage);
                    await ReloadAfterChangeAsync();
                }
                else
                {
                    // Mode hors ligne : sauvegarder localement
                    await SaveDecisionOfflineAsync(leave, "approve");
                    SuccessMessage = "Décision enregistrée. Elle sera synchronisée dès que la connexion sera rétablie.";
                    NotificationRequested?.Invoke(this, SuccessMessage);

                    // Retirer de la liste locale
                    Leaves.Remove(leave);
                    PendingDecisionsCount++;
                }
            }
            catch (Exception ex)
            {
                // En cas d'erreur réseau, sauvegarder localement
                if (IsNetworkError(ex))
                {
                    await SaveDecisionOfflineAsync(leave, "approve");
                    SuccessMessage = "Connexion perdue. Décision enregistrée localement.";
                    NotificationRequested?.Invoke(this, SuccessMessage);
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
                OnPropertyChanged(nameof(IsManager));
            }
        }

        private async Task RefuseAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                if (IsOnline)
                {
                    // Mode en ligne : envoyer directement à Odoo
                    await _odoo.RefuseLeaveAsync(leave.Id);
                    SuccessMessage = "La demande de congé a été refusée avec succès !";
                    NotificationRequested?.Invoke(this, SuccessMessage);
                    await ReloadAfterChangeAsync();
                }
                else
                {
                    // Mode hors ligne : sauvegarder localement
                    await SaveDecisionOfflineAsync(leave, "refuse");
                    SuccessMessage = "Décision enregistrée. Elle sera synchronisée dès que la connexion sera rétablie.";
                    NotificationRequested?.Invoke(this, SuccessMessage);

                    // Retirer de la liste locale
                    Leaves.Remove(leave);
                    PendingDecisionsCount++;
                }
            }
            catch (Exception ex)
            {
                // En cas d'erreur réseau, sauvegarder localement
                if (IsNetworkError(ex))
                {
                    await SaveDecisionOfflineAsync(leave, "refuse");
                    SuccessMessage = "Connexion perdue. Décision enregistrée localement.";
                    NotificationRequested?.Invoke(this, SuccessMessage);
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
                OnPropertyChanged(nameof(IsManager));
            }
        }

        private async Task SaveDecisionOfflineAsync(LeaveToApprove leave, string decisionType)
        {
            PendingLeaveDecision decision = new()
            {
                ManagerUserId = ManagerUserId,
                LeaveId = leave.Id,
                DecisionType = decisionType,
                EmployeeName = leave.EmployeeName,
                LeaveStartDate = leave.StartDate,
                LeaveEndDate = leave.EndDate,
                SyncStatus = SyncStatus.Pending
            };

            await _databaseService.AddPendingLeaveDecisionAsync(decision);
            System.Diagnostics.Debug.WriteLine($"Décision {decisionType} sauvegardée offline pour congé {leave.Id}");
        }

        private static bool IsNetworkError(Exception ex)
        {
            return ex is HttpRequestException ||
                   ex.InnerException is HttpRequestException ||
                   ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ReloadAfterChangeAsync()
        {
            List<LeaveToApprove> list = await _odoo.GetLeavesToApproveAsync();

            // Exclure les congés avec décision en attente
            List<PendingLeaveDecision> pendingDecisions = await _databaseService.GetPendingLeaveDecisionsAsync(ManagerUserId);
            HashSet<int> pendingLeaveIds = pendingDecisions.Select(d => d.LeaveId).ToHashSet();
            list = list.Where(l => !pendingLeaveIds.Contains(l.Id)).ToList();

            // Marquer comme vues
            await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));

            Leaves.Clear();
            foreach (LeaveToApprove item in list)
                Leaves.Add(item);

            if (Leaves.Count == 0 && PendingDecisionsCount == 0)
                ErrorMessage = "Aucune demande de congé en attente.";
        }

        private void RaiseAllCanExecuteChanged()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApproveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefuseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
