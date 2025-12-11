

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Networking;
using PFE.Models;
using PFE.Services;
using static PFE.Helpers.LeaveStatusHelper;

namespace PFE.ViewModels
{
    public class StatusItem
    {
        public string LabelFr { get; init; } = default!;
        public string? ValueEn { get; init; }
        public override string ToString() => LabelFr;
    }

    public class YearItem
    {
        public string Label { get; init; } = default!;
        public int? Value { get; init; }
        public override string ToString() => Label;
    }

    public class LeaveViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private bool _isOffline;
        private string _syncMessage = string.Empty;

        public ObservableCollection<YearItem> Years { get; } = new();
        public ObservableCollection<StatusItem> Statuses { get; } = new();
        private YearItem? _selectedYearItem;
        public YearItem? SelectedYearItem
        {
            get => _selectedYearItem;
            set
            {
                if (_selectedYearItem == value) return;
                _selectedYearItem = value;
                OnPropertyChanged();
                if (!IsBusy) _ = LoadAsync();
            }
        }

        private StatusItem? _selectedStatusItem;
        public StatusItem? SelectedStatusItem
        {
            get => _selectedStatusItem;
            set
            {
                if (_selectedStatusItem == value) return;
                _selectedStatusItem = value;
                OnPropertyChanged();
                if (!IsBusy) _ = LoadAsync();
            }
        }
        public int? SelectedYear => SelectedYearItem?.Value;
        public string? SelectedStateEn => SelectedStatusItem?.ValueEn;

        public bool IsOffline
        {
            get => _isOffline;
            private set { _isOffline = value; OnPropertyChanged(); }
        }

        public string SyncMessage
        {
            get => _syncMessage;
            private set { _syncMessage = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveViewModel(OdooClient odooClient, OfflineService offlineService)
        {
            _odooClient = odooClient;
            _offlineService = offlineService;
            Leaves = new ObservableCollection<Leave>();
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);

            InitializeYears();
            InitializeStatuses();

            // S'abonner aux événements de synchronisation
            _offlineService.SyncStatusChanged += OnSyncStatusChanged;
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }

        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;
        public ObservableCollection<Leave> Leaves { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }

        public async Task LoadAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] LoadAsync appelé. IsBusy={IsBusy}");

            if (IsBusy)
            {
                System.Diagnostics.Debug.WriteLine("[LeaveViewModel] LoadAsync ignoré car IsBusy=true");
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

            try
            {
                if (!IsEmployee)
                {
                    ErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    Leaves.Clear();
                    return;
                }

                if (IsOffline)
                {
                    ErrorMessage = "Mode hors-ligne. Connectez-vous pour voir vos congés.";
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Chargement des congés... State={SelectedStateEn}");
                List<Leave> list = await _odooClient.GetLeavesAsync(SelectedStateEn);
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] {list.Count} congés récupérés");

                Leaves.Clear();
                foreach (Leave item in list)
                    Leaves.Add(item);

                if (Leaves.Count == 0)
                    ErrorMessage = "Aucun congé trouvé.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Erreur: {ex.Message}");
                ErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        private async void OnSyncStatusChanged(object? sender, SyncStatusEventArgs e)
        {
            // Quand la synchronisation est terminée avec succès, rafraîchir la liste
            if (e.IsComplete && e.SuccessCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Synchronisation terminée: {e.SuccessCount} succès");

                // Exécuter sur le thread principal
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    SyncMessage = $"✓ {e.SuccessCount} congé{(e.SuccessCount > 1 ? "s synchronisé" : " synchronisé")}s";

                    // Attendre un court instant pour que Odoo traite la demande
                    await Task.Delay(1500);

                    // Rafraîchir la liste des congés
                    System.Diagnostics.Debug.WriteLine("[LeaveViewModel] Rafraîchissement de la liste après sync...");
                    await LoadAsync();

                    // Effacer le message après un moment
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        await MainThread.InvokeOnMainThreadAsync(() => SyncMessage = string.Empty);
                    });
                });
            }
            else if (e.PendingCount > 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    SyncMessage = $"⏳ {e.PendingCount} demande{(e.PendingCount > 1 ? "s" : "")} en attente");
            }
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                IsOffline = e.NetworkAccess != NetworkAccess.Internet;

                if (e.NetworkAccess == NetworkAccess.Internet)
                {
                    System.Diagnostics.Debug.WriteLine("[LeaveViewModel] Connexion rétablie, rafraîchissement...");
                    // Connexion rétablie : rafraîchir les données
                    await LoadAsync();
                }
            });
        }

        private void InitializeYears()
        {
            Years.Clear();

            int y = DateTime.Now.Year;

            Years.Add(new YearItem { Label = "(Toutes)", Value = null });
            Years.Add(new YearItem { Label = (y - 1).ToString(), Value = y - 1 });
            Years.Add(new YearItem { Label = y.ToString(), Value = y });
            Years.Add(new YearItem { Label = (y + 1).ToString(), Value = y + 1 });

            SelectedYearItem = Years.FirstOrDefault(it => it.Value == y);
        }

        private void InitializeStatuses()
        {
            Statuses.Clear();

            Statuses.Add(new StatusItem { LabelFr = "(Tous)", ValueEn = null });

            foreach (KeyValuePair<string, string> kv in FrenchToEnglishStatus)
                Statuses.Add(new StatusItem { LabelFr = kv.Key, ValueEn = kv.Value });

            SelectedStatusItem = Statuses[0];
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}