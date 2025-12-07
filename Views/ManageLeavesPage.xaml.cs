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
}
