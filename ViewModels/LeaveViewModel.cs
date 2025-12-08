using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Services;

namespace PFE.ViewModels
{
    public class LeaveViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private bool _isBusy;
        private string _errorMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;
            Leaves = new ObservableCollection<Leave>();
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        }

        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public ObservableCollection<Leave> Leaves { get; }

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

        public async Task LoadAsync()
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    ErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    Leaves.Clear();
                    return;
                }

                List<Leave> list = await _odooClient.GetLeavesAsync();

                Leaves.Clear();

                foreach (Leave item in list)
                    Leaves.Add(item);

                if (Leaves.Count == 0)
                    ErrorMessage = "Aucun congé trouvé.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
