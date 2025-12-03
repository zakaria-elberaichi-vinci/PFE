namespace PFE;

public partial class MainPage : ContentPage
{
    // ⬇️ URL & DB EN DUR ICI
    private const string OdooUrl = "https://ipl-pfe-2025-groupe11.odoo.com";
    private const string OdooDb = "ipl-pfe-2025-groupe11-main-26040231";

    private OdooClient _client;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        ResultLabel.Text = "Connexion en cours...";
        ResultLabel.TextColor = Colors.Black;

        string login = LoginEntry.Text?.Trim();
        string password = PasswordEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(login) ||
            string.IsNullOrWhiteSpace(password))
        {
            ResultLabel.Text = "Veuillez remplir login et mot de passe.";
            ResultLabel.TextColor = Colors.Red;
            return;
        }

        _client = new OdooClient(OdooUrl);

        try
        {
            var (success, raw) = await _client.LoginAsync(OdooDb, login, password);

            if (success)
            {
                ResultLabel.Text = "✅ Connexion réussie à Odoo !";
                ResultLabel.TextColor = Colors.Green;

                // On peut garder les infos globalement si tu veux les réutiliser
                App.UserId = 0;               // pour l'instant on ne récupère pas encore l'id
                App.UserPassword = password;  // si tu veux le réutiliser ensuite

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
