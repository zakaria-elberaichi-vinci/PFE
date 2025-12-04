using Microsoft.Maui.Controls;

namespace PFE;

public partial class LeaveRequestPage : ContentPage
{
    private readonly OdooClient _odooClient;

    public LeaveRequestPage(string odooUrl, string odooDb, int userId, string userPassword)
    {
        InitializeComponent();

        _odooClient = new OdooClient(odooUrl, odooDb, userId, userPassword);

        StartDatePicker.Date = DateTime.Today;
        EndDatePicker.Date = DateTime.Today;

        LeaveTypePicker.ItemsSource = new List<string>
        {
            "Congé payé",
            "Congé maladie",
            "Sans solde"
        };
    }

    private async void SubmitButton_Clicked(object sender, EventArgs e)
    {
        if (LeaveTypePicker.SelectedItem == null)
        {
            await DisplayAlert("Erreur", "Veuillez choisir un type de congé.", "OK");
            return;
        }

        if (EndDatePicker.Date < StartDatePicker.Date)
        {
            await DisplayAlert("Erreur",
                "La date de fin ne peut pas être avant la date de début.",
                "OK");
            return;
        }

        string typeConge = LeaveTypePicker.SelectedItem.ToString()!;
        string motif = ReasonEditor.Text?.Trim() ?? string.Empty;

        try
        {
            SubmitButton.IsEnabled = false;
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            int leaveTypeId = typeConge switch
            {
                "Congé payé" => 1, // ⚠️ à adapter aux ID réels dans Odoo
                "Congé maladie" => 2,
                "Sans solde" => 3,
                _ => 1
            };

            // Pour l’instant on laisse employeeId à 0
            int createdId = await _odooClient.CreateLeaveRequestAsync(
                leaveTypeId: leaveTypeId,
                     startDate: StartDatePicker.Date,
     endDate: EndDatePicker.Date,
     reason: motif
 );

            await DisplayAlert(
                "Demande envoyée",
                $"Votre demande de congé a été créée dans Odoo (id = {createdId}).",
                "OK");

            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erreur",
                "Impossible d'envoyer la demande de congé.\n\nDétails : " + ex.Message,
                "OK");
        }
        finally
        {
            SubmitButton.IsEnabled = true;
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
}
