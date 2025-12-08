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

        private string _name = string.Empty;
        private string? _workEmail;
        private string? _jobTitle;
        private string? _departmentName;
        private string? _managerName;

        private bool _isBusy;
        private string _errorMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public UserProfileViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
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
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;

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

        private void Clear()
        {
            Name = string.Empty;
            WorkEmail = null;
            JobTitle = null;
            DepartmentName = null;
            ManagerName = null;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
