using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Services;

namespace PFE.ViewModels
{
    public class ManageLeavesViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odoo;
        private readonly ILeaveNotificationService _notificationService;
        private bool _isBusy;
        private string _errorMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ManageLeavesViewModel(OdooClient odoo, ILeaveNotificationService notificationService)
        {
            _odoo = odoo;
            _notificationService = notificationService;

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
        }

        public bool IsManager => _odoo.session.Current.IsManager;

        private int ManagerUserId => _odoo.session.Current.UserId ?? 0;

        public ObservableCollection<LeaveToApprove> Leaves { get; }
        public List<LeaveToApprove> NewLeaves { get; private set; } = [];
        public bool HasNewLeaves => NewLeaves.Count > 0;

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

        public ICommand RefreshCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RefuseCommand { get; }

        public event EventHandler<string>? NotificationRequested;

        public async Task LoadAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            NewLeaves.Clear();

            try
            {
                if (!IsManager)
                {
                    ErrorMessage = "Veuillez vous connecter en tant que manager.";
                    Leaves.Clear();
                    return;
                }

                List<LeaveToApprove> list = await _odoo.GetLeavesToApproveAsync();
                HashSet<int> seenIds = await _notificationService.GetSeenLeaveIdsAsync(ManagerUserId);
                NewLeaves = list.Where(l => !seenIds.Contains(l.Id)).ToList();
                await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));

                Leaves.Clear();
                foreach (LeaveToApprove item in list)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0)
                {
                    ErrorMessage = "Aucune demande de congé en attente.";
                }

                OnPropertyChanged(nameof(NewLeaves));
                OnPropertyChanged(nameof(HasNewLeaves));
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
            if (leave == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                await _odoo.ApproveLeaveAsync(leave.Id);
                NotificationRequested?.Invoke(this, "La demande de congé a été validée avec succès !");
                await ReloadAfterChangeAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de valider la demande : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsManager));
            }
        }

        private async Task RefuseAsync(LeaveToApprove? leave)
        {
            if (leave == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                await _odoo.RefuseLeaveAsync(leave.Id);
                NotificationRequested?.Invoke(this, "La demande de congé a été refusée avec succès !");
                await ReloadAfterChangeAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de refuser la demande : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsManager));
            }
        }

        private async Task ReloadAfterChangeAsync()
        {
            List<LeaveToApprove> list = await _odoo.GetLeavesToApproveAsync();
            await _notificationService.MarkLeavesAsSeenAsync(ManagerUserId, list.Select(l => l.Id));

            Leaves.Clear();
            foreach (LeaveToApprove item in list)
            {
                Leaves.Add(item);
            }

            if (Leaves.Count == 0)
            {
                ErrorMessage = "Aucune demande de congé en attente.";
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
    }
}