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

        // Source de vérité globale lue depuis le client
        public bool IsAuthenticated => _odooClient.IsAuthenticated;

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
                if (!IsAuthenticated)
                {
                    ErrorMessage = "Vous n'êtes pas connecté.";
                    Leaves.Clear();
                    return;
                }

                List<Leave> list = await _odooClient.GetLeavesAsync();
                Leaves.Clear();
                foreach (Leave item in list)
                    Leaves.Add(item);

                if (Leaves.Count == 0)
                    ErrorMessage = "Aucun congé trouvé pour cet utilisateur.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsAuthenticated));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}