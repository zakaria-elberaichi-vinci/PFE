



using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Services;
using static PFE.Helpers.LeaveStatusHelper;

namespace PFE.ViewModels
{
    public class StatusItem
    {
        public string LabelFr { get; init; } = default!;
        public string? ValueEn { get; init; }
        public override string ToString()
        {
            return LabelFr;
        }
    }

    public class YearItem
    {
        public string Label { get; init; } = default!;
        public int? Value { get; init; }
        public override string ToString()
        {
            return Label;
        }
    }

    public class LeaveViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private readonly OfflineService _offlineService;
        private readonly IDatabaseService _databaseService;
        private int _employeeId;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private bool _isOffline;
        private string _syncMessage = string.Empty;

        public ObservableCollection<YearItem> Years { get; } = [];
        public ObservableCollection<StatusItem> Statuses { get; } = [];
        private YearItem? _selectedYearItem;
        public YearItem? SelectedYearItem
        {
            get => _selectedYearItem;
            set
            {
                if (_selectedYearItem == value)
                {
                    return;
                }

                _selectedYearItem = value;
                OnPropertyChanged();
                if (!IsBusy)
                {
                    _ = LoadAsync();
                }
            }
        }

        private StatusItem? _selectedStatusItem;
        public StatusItem? SelectedStatusItem
        {
            get => _selectedStatusItem;
            set
            {
                if (_selectedStatusItem == value)
                {
                    return;
                }

                _selectedStatusItem = value;
                OnPropertyChanged();
                if (!IsBusy)
                {
                    _ = LoadAsync();
                }
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

        public LeaveViewModel(OdooClient odooClient, OfflineService offlineService, IDatabaseService databaseService)
        {
            _odooClient = odooClient;
            _offlineService = offlineService;
            _databaseService = databaseService;
            Leaves = [];
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

                // Initialiser l'employeeId en premier (important pour le cache)
                if (_odooClient.session.Current.EmployeeId.HasValue)
                {
                    _employeeId = _odooClient.session.Current.EmployeeId.Value;
                }

                if (IsOffline)
                {
                    // Mode hors-ligne : charger depuis le cache
                    System.Diagnostics.Debug.WriteLine("[LeaveViewModel] Mode hors-ligne, chargement depuis le cache...");
                    await LoadFromCacheAsync();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Chargement des congés... State={SelectedStateEn}");
                List<Leave> list = await _odooClient.GetLeavesAsync(SelectedStateEn);
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] {list.Count} congés récupérés");

                // Sauvegarder dans le cache (tous les congés, pas seulement ceux filtrés)
                if (string.IsNullOrEmpty(SelectedStateEn) && _employeeId > 0)
                {
                    await _databaseService.SaveLeavesAsync(_employeeId, list);
                }

                // Filtrer par année si nécessaire
                if (SelectedYear.HasValue)
                {
                    list = list.Where(l => l.StartDate.Year == SelectedYear.Value).ToList();
                }

                Leaves.Clear();
                foreach (Leave item in list)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0)
                {
                    ErrorMessage = "Aucun congé trouvé.";
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Erreur réseau HttpRequestException: {ex.Message}");
                IsOffline = true;
                
                // Charger depuis le cache en cas d'erreur réseau
                if (_employeeId > 0)
                {
                    await LoadFromCacheAsync();
                }
                else
                {
                    ErrorMessage = "Mode hors-ligne. Connectez-vous une première fois pour mettre en cache vos congés.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Erreur générale: {ex.GetType().Name} - {ex.Message}");
                
                // Vérifier si c'est une erreur réseau (inclut les exceptions enveloppées)
                bool isNetworkError = ex.InnerException is HttpRequestException ||
                                      ex.Message.Contains("Hôte inconnu") ||
                                      ex.Message.Contains("host") ||
                                      ex.Message.Contains("network") ||
                                      ex.Message.Contains("connection") ||
                                      Connectivity.Current.NetworkAccess != NetworkAccess.Internet;

                if (isNetworkError && _employeeId > 0)
                {
                    System.Diagnostics.Debug.WriteLine("[LeaveViewModel] Erreur réseau détectée, chargement depuis le cache...");
                    IsOffline = true;
                    await LoadFromCacheAsync();
                }
                else if (isNetworkError)
                {
                    IsOffline = true;
                    ErrorMessage = "Mode hors-ligne. Connectez-vous une première fois pour mettre en cache vos congés.";
                }
                else
                {
                    ErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
                }
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        private async Task LoadFromCacheAsync()
        {
            try
            {
                // Vérifier que l'employeeId est initialisé
                if (_employeeId <= 0)
                {
                    ErrorMessage = "Aucun congé en cache. Connectez-vous une première fois pour charger vos congés.";
                    return;
                }

                // Convertir le statut français pour le filtrage du cache
                string? statusFilter = SelectedStatusItem?.LabelFr;
                if (statusFilter == "(Tous)")
                {
                    statusFilter = null;
                }

                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Chargement cache pour employé {_employeeId}, statut={statusFilter}, année={SelectedYear}");

                List<Leave> cachedLeaves = await _databaseService.GetCachedLeavesAsync(
                    _employeeId,
                    statusFilter,
                    SelectedYear
                );

                Leaves.Clear();
                foreach (Leave item in cachedLeaves)
                {
                    Leaves.Add(item);
                }

                if (Leaves.Count == 0)
                {
                    ErrorMessage = "Aucun congé en cache. Connectez-vous pour charger vos congés.";
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] {Leaves.Count} congés chargés depuis le cache");
                    ErrorMessage = string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LeaveViewModel] Erreur chargement cache: {ex.Message}");
                ErrorMessage = "Impossible de charger les congés depuis le cache.";
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
                        _ = await MainThread.InvokeOnMainThreadAsync(() => SyncMessage = string.Empty);
                    });
                });
            }
            else if (e.PendingCount > 0)
            {
                _ = await MainThread.InvokeOnMainThreadAsync(() =>
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
                    // Connexion rétablie : rafraîchir les données depuis le serveur
                    await LoadAsync();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[LeaveViewModel] Connexion perdue, chargement depuis le cache...");
                    // Connexion perdue : charger depuis le cache
                    await LoadFromCacheAsync();
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
            {
                Statuses.Add(new StatusItem { LabelFr = kv.Key, ValueEn = kv.Value });
            }

            SelectedStatusItem = Statuses[0];
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}