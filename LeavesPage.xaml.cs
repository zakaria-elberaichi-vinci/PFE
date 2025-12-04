using System;
using System.Collections.Generic;

namespace PFE;

public partial class LeavesPage : ContentPage
{
    private readonly OdooClient _client;
    private readonly string _db;
    private readonly int _userId;
    private readonly string _password;

    public LeavesPage(string baseUrl, string db, int userId, string password)
    {
        InitializeComponent();

        _client = new OdooClient(baseUrl);
        _db = db;
        _userId = userId;
        _password = password;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            var leaves = await _client.GetLeavesAsync(_db, _userId, _password);

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


