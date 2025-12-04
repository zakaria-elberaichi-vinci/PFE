using System;
using System.Collections.Generic;
using PFE.Services;

namespace PFE;

public partial class LeavesPage : ContentPage
{
    private readonly OdooConfigService _configService;
    private readonly OdooClient _client;

    public LeavesPage(OdooConfigService configService, OdooClient client)
    {
        InitializeComponent();

        _configService = configService;
        _client = client;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            var leaves = await _client.GetLeavesAsync();

            if (leaves.Count == 0)
            {
                await DisplayAlert("Info", "Aucun congé trouvé pour cet utilisateur.", "OK");
            }

            LeavesCollection.ItemsSource = leaves;
        }
        catch (Exception ex)
        {
            // on affiche le message complet pour voir la réponse Odoo
            await DisplayAlert("Erreur",
                "Impossible de charger vos congés :\n\n" + ex.Message,
                "OK");
        }
    }
}


