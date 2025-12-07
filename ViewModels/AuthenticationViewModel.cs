using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Services;

namespace PFE.ViewModels
{
    public class AuthenticationViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;

        private string _login = string.Empty;
        private string _password = string.Empty;
        private bool _isBusy;
        private string _errorMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AuthenticationViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;
            LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => !IsBusy);
        }

        public string Login
        {
            get => _login;
            set { _login = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(); }
        }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;

        public int? UserId => _odooClient.session.Current.UserId;

        public bool IsManager => _odooClient.session.Current.IsManager;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand LoginCommand { get; }

        public Action? OnLoginSucceeded { get; set; }

        private async Task LoginAsync()
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                bool success = await _odooClient.LoginAsync(Login, Password);

                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserId));

                if (success)
                    OnLoginSucceeded?.Invoke();
                else
                    ErrorMessage = "Échec de connexion. Vérifiez vos identifiants.";

            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
