using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Services;

namespace PFE.ViewModels
{
    public class UserProfileViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly IDatabaseService _databaseService;

        private string _name = string.Empty;
        private string? _workEmail;
        private string? _jobTitle;
        private string? _departmentName;
        private string? _managerName;

        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _successMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public UserProfileViewModel(OdooClient odooClient, IDatabaseService databaseService)
        {
            _odooClient = odooClient;
            _databaseService = databaseService;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
            ClearCacheCommand = new RelayCommand(async _ => await ClearCacheAsync(), _ => !IsBusy);
        }

        public string Name
        {
            get => _name;
            private set { _name = value; OnPropertyChanged(); }
        }

        public string? WorkEmail
        {
            get => _workEmail;
            private set { _workEmail = value; OnPropertyChanged(); }
        }

        public string? JobTitle
        {
            get => _jobTitle;
            private set { _jobTitle = value; OnPropertyChanged(); }
        }

        public string? DepartmentName
        {
            get => _departmentName;
            private set { _departmentName = value; OnPropertyChanged(); }
        }

        public string? ManagerName
        {
            get => _managerName;
            private set { _managerName = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ClearCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        public string SuccessMessage
        {
            get => _successMessage;
            private set { _successMessage = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearCacheCommand { get; }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;

        private int UserId => _odooClient.session.Current.UserId ?? 0;

        public async Task LoadAsync()
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                if (!IsAuthenticated)
                {
                    ErrorMessage = "Veuillez vous connecter pour accéder à vos informations.";
                    Clear();
                    return;
                }

                UserProfile profile = await _odooClient.GetUserInfosAsync();
                if (profile == null)
                {
                    ErrorMessage = "Profil introuvable.";
                    Clear();
                    return;
                }

                Name = profile.Name;
                WorkEmail = profile.WorkEmail;
                JobTitle = profile.JobTitle;
                DepartmentName = profile.DepartmentName;
                ManagerName = profile.ManagerName;
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
                Clear();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur : {ex.Message}";
                Clear();
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsAuthenticated));
            }
        }

        /// <summary>
        /// Vide le cache local (décisions en attente, demandes en cache, etc.)
        /// </summary>
        private async Task ClearCacheAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                await _databaseService.InitializeAsync();

                // Vider le cache des demandes à approuver (si manager)
                if (_odooClient.session.Current.IsManager)
                {
                    await _databaseService.ClearLeavesToApproveCacheAsync(UserId);
                    
                    // Supprimer toutes les décisions de ce manager
                    var allDecisions = await _databaseService.GetAllLeaveDecisionsAsync(UserId);
                    foreach (var decision in allDecisions)
                    {
                        await _databaseService.DeletePendingLeaveDecisionAsync(decision.Id);
                    }
                }

                // Vider les notifications vues
                await _databaseService.ClearSeenNotificationsAsync(UserId);

                // Vider les notifications de changement de statut (pour employés)
                if (_odooClient.session.Current.EmployeeId.HasValue)
                {
                    await _databaseService.ClearNotifiedLeavesAsync(_odooClient.session.Current.EmployeeId.Value);
                }

                SuccessMessage = "✓ Cache vidé avec succès !";
                System.Diagnostics.Debug.WriteLine("UserProfileViewModel: Cache vidé");

                // Effacer le message après quelques secondes
                _ = ClearSuccessMessageAfterDelayAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur lors du vidage du cache : {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"UserProfileViewModel: Erreur ClearCache - {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ClearSuccessMessageAfterDelayAsync()
        {
            await Task.Delay(5000);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SuccessMessage = string.Empty;
            });
        }

        private void Clear()
        {
            Name = string.Empty;
            WorkEmail = null;
            JobTitle = null;
            DepartmentName = null;
            ManagerName = null;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}