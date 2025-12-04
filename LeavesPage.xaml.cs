using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;

namespace PFE;

public partial class LeavesPage : ContentPage
{
    private readonly OdooClient _odooClient;

    public ObservableCollection<LeaveRecord> Leaves { get; } = new();

    public LeavesPage(string odooUrl, string odooDb, int userId, string userPassword)
    {
        InitializeComponent();

        _odooClient = new OdooClient(odooUrl, odooDb, userId, userPassword);

        LeavesCollectionView.ItemsSource = Leaves;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadLeavesAsync();
    }

    private async Task LoadLeavesAsync()
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            EmptyLabel.IsVisible = false;
            Leaves.Clear();

            var leaves = await _odooClient.GetLeavesAsync();

            foreach (var leave in leaves)
                Leaves.Add(leave);

            EmptyLabel.IsVisible = Leaves.Count == 0;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur",
                "Impossible de charger les congés.\n\nDétails : " + ex.Message,
                "OK");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        }
    }
}
