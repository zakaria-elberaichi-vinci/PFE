
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Helpers;
using PFE.Services;
using static PFE.Helpers.LeaveTypeHelper;

namespace PFE.ViewModels
{
    public class LeaveRequestViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;

        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today;
        private ObservableCollection<LeaveTypeItem> _leaveTypes = new();
        private LeaveTypeItem? _selectedLeaveType;
        private string _reason = string.Empty;

        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;

        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private bool _isLeaveTypeEnabled;
        private bool _isAccessDenied;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveRequestViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;

            RefreshCommand = new RelayCommand(
                async _ => await RefreshLeaveTypesAsync(),
                _ => !IsBusy && AreDatesValid && IsEmployee
            );

            SubmitCommand = new RelayCommand(
                async _ => await SubmitAsync(),
                _ => !IsBusy && AreDatesValid && SelectedLeaveType != null && IsEmployee
            );
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (Set(ref _startDate, value))
                {
                    _ = OnDatesChangedAsync();
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                if (Set(ref _endDate, value))
                {
                    _ = OnDatesChangedAsync();
                }
            }
        }

        public ObservableCollection<LeaveTypeItem> LeaveTypes
        {
            get => _leaveTypes;
            private set => Set(ref _leaveTypes, value);
        }

        public LeaveTypeItem? SelectedLeaveType
        {
            get => _selectedLeaveType;
            set
            {
                if (Set(ref _selectedLeaveType, value))
                {
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Reason
        {
            get => _reason;
            set => Set(ref _reason, value);
        }

        public int TotalAllocated
        {
            get => _totalAllocated;
            private set => Set(ref _totalAllocated, value);
        }

        public int TotalTaken
        {
            get => _totalTaken;
            private set => Set(ref _totalTaken, value);
        }

        public int TotalRemaining
        {
            get => _totalRemaining;
            private set => Set(ref _totalRemaining, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => Set(ref _errorMessage, value);
        }

        public bool IsLeaveTypeEnabled
        {
            get => _isLeaveTypeEnabled;
            private set => Set(ref _isLeaveTypeEnabled, value);
        }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;
        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public bool IsAccessDenied
        {
            get => _isAccessDenied;
            private set => Set(ref _isAccessDenied, value);
        }

        public bool AreDatesValid => StartDate != default && EndDate != default && EndDate >= StartDate;

        public ICommand RefreshCommand { get; }
        public ICommand SubmitCommand { get; }

        public async Task LoadAsync()
        {
            ErrorMessage = string.Empty;

            if (!IsEmployee)
            {
                IsAccessDenied = true;
                ErrorMessage = "Accès refusé : cette page est réservée aux employés connectés.";
                return;
            }

            if (StartDate == default) StartDate = DateTime.Today;
            if (EndDate == default) EndDate = DateTime.Today;

            await RefreshLeaveTypesAsync();
        }

        private async Task OnDatesChangedAsync()
        {
            IsLeaveTypeEnabled = AreDatesValid && IsEmployee;

            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();

            if (!AreDatesValid)
            {
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
                return;
            }

            await RefreshLeaveTypesAsync();
        }

        public async Task RefreshLeaveTypesAsync()
        {
            if (!IsEmployee)
            {
                IsAccessDenied = true;
                ErrorMessage = "Accès refusé : cette page est réservée aux employés connectés.";
                return;
            }

            if (!AreDatesValid)
            {
                IsLeaveTypeEnabled = false;
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                HashSet<int> allowedIds = await _odooClient.GetAllowedLeaveTypeIdsAsync(StartDate, EndDate);
                List<LeaveTypeItem> filtered = FilterLeaveTypeItems(allowedIds);

                LeaveTypes = new ObservableCollection<LeaveTypeItem>(filtered);
                SelectedLeaveType = LeaveTypes.FirstOrDefault();
                IsLeaveTypeEnabled = LeaveTypes.Count > 0;

                (int totalAlloue, int totalPris) = await _odooClient.GetNumberTimeOffAsync();
                TotalAllocated = totalAlloue;
                TotalTaken = totalPris;
                TotalRemaining = Math.Max(0, TotalAllocated - TotalTaken);

                if (LeaveTypes.Count == 0)
                {
                    ErrorMessage = "Aucun type de congé disponible pour la période choisie. Veuillez demander une allocation.";
                    IsLeaveTypeEnabled = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.\n\nDétails : " + ex.Message;
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
                IsLeaveTypeEnabled = false;

                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
            }
            finally
            {
                IsBusy = false;
            }
        }


        public async Task RefreshTotalsAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                (int allocated, int taken) = await _odooClient.GetNumberTimeOffAsync();

                TotalAllocated = allocated;
                TotalTaken = taken;
                TotalRemaining = allocated - taken;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de rafraîchir les totaux de congés.\n\nDétails : " + ex.Message;

                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
            }
            finally
            {
                IsBusy = false;
            }
        }


        public async Task<(bool Success, int? CreatedId, string Message)> SubmitAsync()
        {
            if (!IsEmployee)
            {
                return (false, null, "Accès refusé.");
            }
            if (!AreDatesValid)
            {
                return (false, null, "La date de fin ne peut pas être avant la date de début.");
            }
            if (SelectedLeaveType is null)
            {
                return (false, null, "Veuillez choisir un type de congé.");
            }

            try
            {
                IsBusy = true;

                int createdId = await _odooClient.CreateLeaveRequestAsync(
                    leaveTypeId: SelectedLeaveType.Id,
                    startDate: StartDate,
                    endDate: EndDate,
                    reason: Reason?.Trim() ?? string.Empty
                );

                return (true, createdId, $"Votre demande de congé a été créée dans Odoo (id = {createdId}).");
            }
            catch (Exception ex)
            {
                return (false, null, "Impossible d'envoyer la demande de congé.\n\nDétails : " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }
}