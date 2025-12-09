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
        private CancellationTokenSource? _datesCts;
        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private bool _isAccessDenied;
        private bool _hasOverlap;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveRequestViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;

            RefreshCommand = new RelayCommand(
                async _ => await RefreshTotalsAsync(),
                _ => !IsBusy && AreDatesValid && IsEmployee
            );

            SubmitCommand = new RelayCommand(
                async _ => await SubmitAsync(),
                _ => CanSubmit
            );
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                if (Set(ref _startDate, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AreDatesValid)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
                    (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AreDatesValid)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
                    (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
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
            private set
            {
                if (Set(ref _totalRemaining, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => Set(ref _errorMessage, value);
        }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;
        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public bool IsAccessDenied
        {
            get => _isAccessDenied;
            private set => Set(ref _isAccessDenied, value);
        }

        public bool AreDatesValid => StartDate != default && EndDate != default && EndDate >= StartDate;

        public bool HasOverlap
        {
            get => _hasOverlap;
            private set => Set(ref _hasOverlap, value);
        }

        public bool CanSubmit => !IsBusy && AreDatesValid && SelectedLeaveType != null && IsEmployee;

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

            await LoadLeaveTypesAsync();
            await RefreshTotalsAsync();
        }

        private async Task OnDatesChangedAsync()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();

            await RefreshTotalsAsync();

            try
            {
                bool overlap = await _odooClient.HasOverlappingLeaveAsync(StartDate, EndDate);
                HasOverlap = overlap;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de vérifier le chevauchement.\n\nDétails : " + ex.Message;
                HasOverlap = false;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task LoadLeaveTypesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                HashSet<int> allowedIds = await _odooClient.GetAllowedLeaveTypeIdsAsync();
                List<LeaveTypeItem> filtered = FilterLeaveTypeItems(allowedIds);
                LeaveTypes = new ObservableCollection<LeaveTypeItem>(filtered);
                SelectedLeaveType = LeaveTypes.FirstOrDefault();

                if (LeaveTypes.Count == 0)
                {
                    ErrorMessage = "Aucun type de congé disponible. Veuillez demander une allocation.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.\n\nDétails : " + ex.Message;
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
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

                (int allocated, int taken) = await _odooClient.GetNumberTimeOffAsync(StartDate.Year);
                TotalAllocated = allocated;
                TotalTaken = taken;
                TotalRemaining = Math.Max(0, TotalAllocated - TotalTaken);
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
                return (false, null, "Accès refusé. Veuillez vous connecter en tant qu'employé");
            }

            if (!AreDatesValid)
            {
                return (false, null, "La date de fin ne peut pas être avant la date de début.");
            }

            if (TotalRemaining == 0)
            {
                return (false, null, "Pas assez d'allocation: votre solde est à 0.");
            }

            try
            {
                bool overlap = await _odooClient.HasOverlappingLeaveAsync(StartDate, EndDate);
                HasOverlap = overlap;
                if (HasOverlap)
                {
                    return (false, null, "Les dates sélectionnées se chevauchent avec un congé existant.");
                }
            }
            catch (Exception ex)
            {
                return (false, null, "Impossible de vérifier le chevauchement.\n\nDétails : " + ex.Message);
            }

            try
            {
                IsBusy = true;

                int createdId = await _odooClient.CreateLeaveRequestAsync(
                    leaveTypeId: SelectedLeaveType!.Id,
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