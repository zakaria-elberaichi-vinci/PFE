
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private bool _isBusy;
        private string _errorMessage = string.Empty;
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

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;
            Leaves = [];
            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);

            InitializeYears();
            InitializeStatuses();
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
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    ErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    Leaves.Clear();
                    return;
                }

                List<Leave> list = await _odooClient.GetLeavesAsync(SelectedYear, SelectedStateEn);
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
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de charger vos congés : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
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