using System.Windows.Input;
using PFE.Services;
using PFE.Views;

namespace PFE.ViewModels
{
    public class AppViewModel
    {
        private readonly OdooClient _odooClient;
        private readonly Func<LoginPage> _loginPageFactory;

        public AppViewModel(OdooClient odooClient, Func<LoginPage> loginPageFactory)
        {
            _odooClient = odooClient;
            _loginPageFactory = loginPageFactory;

            LogoutCommand = new RelayCommand(async _ => await LogoutAsync());
        }

        public ICommand LogoutCommand { get; }

        private async Task LogoutAsync()
        {
            try
            {
                await _odooClient.LogoutAsync();

                LoginPage loginPage = _loginPageFactory();
                Application.Current.MainPage = new NavigationPage(loginPage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logout error: {ex.Message}");
                LoginPage loginPage = _loginPageFactory();
                Application.Current.MainPage = new NavigationPage(loginPage);
            }
        }
    }
}
