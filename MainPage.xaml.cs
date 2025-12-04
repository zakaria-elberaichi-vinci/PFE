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
            var (success, userId, raw) = await _client.LoginAsync(OdooDb, login, password);

            if (success)
            {
                ResultLabel.Text = "✅ Connexion réussie à Odoo !";
                ResultLabel.TextColor = Colors.Green;

                // ⚠️ NE PLUS METTRE 0 ICI
                App.OdooUrl = OdooUrl;
                App.OdooDb = OdooDb;
                App.UserId = userId;      // <-- très important
                App.UserPassword = password;

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