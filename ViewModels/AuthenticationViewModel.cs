using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Services;

namespace PFE.ViewModels
{
    public class AuthenticationViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly IBackgroundNotificationService _backgroundNotificationService;
        private readonly IBackgroundLeaveStatusService _backgroundLeaveStatusService;

        private string _login = string.Empty;
        private string _password = string.Empty;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private bool _rememberMe;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AuthenticationViewModel(
            OdooClient odooClient, 
            IBackgroundNotificationService backgroundNotificationService,
            IBackgroundLeaveStatusService backgroundLeaveStatusService)
        {
            _odooClient = odooClient;
            _backgroundNotificationService = backgroundNotificationService;
            _backgroundLeaveStatusService = backgroundLeaveStatusService;
            LoadRememberedCredentials();
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

        public bool RememberMe
        {
            get => _rememberMe;
            set
            {
                if (_rememberMe == value) return;
                _rememberMe = value;
                OnPropertyChanged();

                SaveRememberPreferenceOnly();
            }
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
                {
                    await PersistCredentialsAsync();

                    // Démarrer le service approprié selon le rôle
                    if (_odooClient.session.Current.IsManager)
                    {
                        // Manager : notifications pour nouvelles demandes de congé
                        _backgroundNotificationService.Start();
                    }
                    else
                    {
                        // Employé : notifications pour acceptation/refus de congé
                        _backgroundLeaveStatusService.Start();
                    }

                    OnLoginSucceeded?.Invoke();
                }
                else
                { ErrorMessage = "Échec de connexion. Vérifiez vos identifiants."; }

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

        private const string RememberMeKey = "auth.rememberme";
        private const string LoginKey = "auth.login";
        private const string PasswordKey = "auth.password";

        private async void LoadRememberedCredentials()
        {
            RememberMe = Preferences.Get(RememberMeKey, false);
            if (RememberMe)
            {
                Login = Preferences.Get(LoginKey, string.Empty);

                try
                {
                    string? pwd = await SecureStorage.GetAsync(PasswordKey);
                    if (!string.IsNullOrEmpty(pwd))
                        Password = pwd;
                }
                catch
                {
                }
            }
        }

        private void SaveRememberPreferenceOnly()
        {
            Preferences.Set(RememberMeKey, RememberMe);
            if (!RememberMe)
            {
                Preferences.Remove(LoginKey);
                SecureStorage.Remove(PasswordKey);
            }
        }

        private async Task PersistCredentialsAsync()
        {
            Preferences.Set(RememberMeKey, RememberMe);

            if (RememberMe)
            {
                Preferences.Set(LoginKey, Login);
                try
                {
                    await SecureStorage.SetAsync(PasswordKey, Password ?? string.Empty);
                }
                catch
                {
                }
            }
            else
            {
                Preferences.Remove(LoginKey);
                SecureStorage.Remove(PasswordKey);
            }
        }


        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
