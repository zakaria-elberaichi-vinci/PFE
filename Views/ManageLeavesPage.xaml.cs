using PFE.Models;
using PFE.Services;

namespace PFE.Views;

public partial class ManageLeavesPage : ContentPage
{
    private readonly OdooClient _odoo;

    public ManageLeavesPage(OdooClient odoo)
    {
        InitializeComponent();
        _odoo = odoo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {

            // Récupérer les données déjà transformées depuis Odoo
            var list = await _odoo.GetLeavesToApproveAsync();

            // Envoyer directement à la CollectionView
            LeavesList.ItemsSource = list;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erreur GetLeavesToApproveAsync : " + ex.Message);
            LeavesList.ItemsSource = new List<LeaveTimeOff>();
        }
    }

    private async void ApproveButton_Clicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is LeaveTimeOff leave)
        {
            try
            {
                // Appel Odoo pour valider la demande
                await _odoo.ApproveLeaveAsync(leave.Id); // il faudra ajouter Id dans le modèle LeaveTimeOff
                LeaveConfirmationPopup.Show("La demande de congé a été validée avec succès !");

                // Rafraîchir la liste
                var leaves = await _odoo.GetLeavesToApproveAsync();
                LeavesList.ItemsSource = leaves;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", "Impossible de valider la demande : " + ex.Message, "OK");
            }
        }
    }

    private async void RefuseButton_Clicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is LeaveTimeOff leave)
        {
            try
            {
                // Appel Odoo pour refuser la demande
                await _odoo.RefuseLeaveAsync(leave.Id); // il faudra ajouter Id dans le modèle LeaveTimeOff
                LeaveConfirmationPopup.Show("La demande de congé a été refusée avec succès !");

                // Rafraîchir la liste
                var leaves = await _odoo.GetLeavesToApproveAsync();
                LeavesList.ItemsSource = leaves;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erreur", "Impossible de refuser la demande : " + ex.Message, "OK");
            }
        }
    }
}
