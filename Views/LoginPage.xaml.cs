using PFE.Services;

namespace PFE;

public partial class LoginPage : ContentPage
{
    private readonly OdooConfigService _configService;
    private readonly OdooClient _client;

    public LoginPage(OdooConfigService configService, OdooClient client)
    {
        InitializeComponent();
        _client = client;
        _configService = configService;
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        ResultLabel.Text = "Connexion en cours...";
        ResultLabel.TextColor = Colors.White;

        string login = LoginEntry.Text?.Trim();
        string password = PasswordEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(login) ||
            string.IsNullOrWhiteSpace(password))
        {
            ResultLabel.Text = "Veuillez remplir login et mot de passe.";
            ResultLabel.TextColor = Colors.Red;
            return;
        }

        try
        {
            var (success, raw) = await _client.LoginAsync(login, password);

            if (success)
            {
                ResultLabel.Text = "✅ Connexion réussie à Odoo !";
                ResultLabel.TextColor = Colors.Green;

                // On peut garder les infos globalement si tu veux les réutiliser
                _configService.UserId = 0;               // pour l'instant on ne récupère pas encore l'id
                _configService.UserPassword = password;  // si tu veux le réutiliser ensuite

                // ⬇⬇⬇ NAVIGATION VERS LA PAGE D'ACCUEIL
                await Navigation.PushAsync(new DashboardPage());

            }

            else
            {
                ResultLabel.Text = "❌ Échec de la connexion.\n\nDétails Odoo :\n" + raw;
                ResultLabel.TextColor = Colors.Red;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = $"Erreur : {ex.Message}";
            ResultLabel.TextColor = Colors.Red;
        }
    }
}
