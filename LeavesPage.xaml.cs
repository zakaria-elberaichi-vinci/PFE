using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;

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
                await DisplayAlert("Info", "Aucun conge trouve pour cet utilisateur.", "OK");
                return;
            }

            // Remplir la VerticalStackLayout avec les items
            LeavesCollection.Children.Clear();
            foreach (var leave in leaves)
            {
                var frame = new Frame
                {
                    Padding = 10,
                    CornerRadius = 8,
                    HasShadow = true,
                    BorderColor = Colors.Transparent,
                    BackgroundColor = Color.FromArgb("#33000000"),  // Noir semi-transparent (20% opacité)
                    Content = new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            new Label
                            {
                                Text = leave.Name,
                                FontAttributes = FontAttributes.Bold,
                                FontSize = 14
                            },
                            new Label
                            {
                                Text = "Periode",
                                FontSize = 10,
                                TextColor = Color.FromArgb("#999999")
                            },
                            new Label
                            {
                                Text = leave.Period,
                                FontSize = 12,
                                TextColor = Color.FromArgb("#666666")
                            },
                            new Label
                            {
                                Text = "Statut",
                                FontSize = 10,
                                TextColor = Color.FromArgb("#999999"),
                                Margin = new Thickness(0, 5, 0, 0)
                            },
                            CreateStatusBadge(leave.Status)
                        }
                    }
                };

                LeavesCollection.Children.Add(frame);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur",
                "Impossible de charger vos conges :\n\n" + ex.Message,
                "OK");
        }
    }

    private Frame CreateStatusBadge(string status)
    {
        var statusColorConverter = new Helpers.StatusColorConverter();
        var backgroundColor = (Color)statusColorConverter.Convert(status, typeof(Color), null, System.Globalization.CultureInfo.CurrentCulture);

        return new Frame
        {
            Padding = new Thickness(8, 4),
            CornerRadius = 4,
            BorderColor = Colors.Transparent,
            HasShadow = false,
            BackgroundColor = backgroundColor,
            Content = new Label
            {
                Text = status,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center
            }
        };
    }
}
