using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Services;
using Syncfusion.Maui.Calendar;
using PFE.Models;
using static PFE.Helpers.DateHelper;

namespace PFE.ViewModels
{
    public class LeaveRequestViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;

        private CalendarDateRange? _selectedRange;

        private ObservableCollection<LeaveTypeItem> _leaveTypes = new();

        private HashSet<DateTime> _blockedDatesSet = new();

        private LeaveTypeItem? _selectedLeaveType;

        private bool _useAllocation;
        private string _reason = string.Empty;
        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _validationMessage = string.Empty;
        private bool _isAccessDenied;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveRequestViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;

            SelectedRange = new CalendarDateRange(DateTime.Today, null);

            RefreshCommand = new RelayCommand(
                async _ => await RefreshTotalsAsync(),
                _ => !IsBusy && AreDatesValid && IsEmployee
            );

            SubmitCommand = new RelayCommand(
                async _ => await SubmitAsync(),
                _ => CanSubmit
            );
        }

        public CalendarDateRange? SelectedRange
        {
            get => _selectedRange;
            set
            {
                _selectedRange = value;
                ValidationMessage = string.Empty;
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(SelectedRange));
                OnPropertyChanged(nameof(AreDatesValid));
                OnPropertyChanged(nameof(CanSubmit));
                if (value?.StartDate != null && value?.EndDate != null)
                {
                    _ = OnDatesChangedAsync();
                }
            }
        }

        public ObservableCollection<LeaveTypeItem> LeaveTypes
        {
            get => _leaveTypes;
            private set
            {
                if (Set(ref _leaveTypes, value))
                    OnPropertyChanged(nameof(LeaveTypes));
            }
        }
        public Func<DateTime, bool>? SelectableDayPredicate { get; private set; }
        public LeaveTypeItem? SelectedLeaveType
        {
            get => _selectedLeaveType;
            set
            {
                if (Set(ref _selectedLeaveType, value))
                {
                    OnPropertyChanged(nameof(SelectedLeaveType));
                    OnPropertyChanged(nameof(CanSubmit));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool UseAllocation
        {
            get => _useAllocation;
            set
            {
                if (Set(ref _useAllocation, value))
                {
                    OnPropertyChanged(nameof(UseAllocation));
                    ValidationMessage = string.Empty;
                    _ = OnUseAllocationChangedAsync();
                }
            }
        }

        public string Reason
        {
            get => _reason;
            set
            {
                if (Set(ref _reason, value))
                    OnPropertyChanged(nameof(Reason));
            }
        }

        public int TotalAllocated
        {
            get => _totalAllocated;
            private set
            {
                if (Set(ref _totalAllocated, value))
                    OnPropertyChanged(nameof(TotalAllocated));
            }
        }

        public int TotalTaken
        {
            get => _totalTaken;
            private set
            {
                if (Set(ref _totalTaken, value))
                    OnPropertyChanged(nameof(TotalTaken));
            }
        }

        public int TotalRemaining
        {
            get => _totalRemaining;
            private set
            {
                if (Set(ref _totalRemaining, value))
                {
                    OnPropertyChanged(nameof(TotalRemaining));
                    OnPropertyChanged(nameof(CanSubmit));
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
                    OnPropertyChanged(nameof(IsBusy));
                    (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(CanSubmit));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (Set(ref _errorMessage, value))
                    OnPropertyChanged(nameof(ErrorMessage));
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (Set(ref _validationMessage, value))
                    OnPropertyChanged(nameof(ValidationMessage));
            }
        }

        public bool IsAuthenticated => _odooClient.session.Current.IsAuthenticated;
        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public bool IsAccessDenied
        {
            get => _isAccessDenied;
            private set
            {
                if (Set(ref _isAccessDenied, value))
                    OnPropertyChanged(nameof(IsAccessDenied));
            }
        }

        public bool AreDatesValid => SelectedRange?.StartDate != null && SelectedRange?.EndDate != null;

        public bool CanSubmit => !IsBusy && AreDatesValid && SelectedLeaveType is not null && !(UseAllocation && TotalRemaining == 0);

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

            await LoadLeaveTypesAsync();
            await RefreshTotalsAsync();
            await LoadBlockedDatesAsync();
        }

        private async Task OnUseAllocationChangedAsync()
        {
            await LoadLeaveTypesAsync();

            if (UseAllocation && TotalRemaining == 0)
            {
                ValidationMessage = "Votre solde d'allocation est de 0.";
            }

            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task OnDatesChangedAsync()
        {
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();

            ValidationMessage = string.Empty;

            await RefreshTotalsAsync();

            if (UseAllocation)
            {
                await LoadLeaveTypesAsync();
                if (TotalRemaining == 0)
                {
                    ValidationMessage = "Votre solde d'allocation est de 0.";
                    return;
                }

            }

            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task LoadBlockedDatesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                List<Leave> leaves = await _odooClient.GetLeavesAsync(null, "confirm", "validate", "validate1", "refuse");

                _blockedDatesSet.Clear();

                foreach (var leave in leaves)
                {
                    var start = leave.StartDate.Date;
                    var end = leave.EndDate.Date;

                    foreach (var day in ExpandRangeToDays(start, end))
                        _blockedDatesSet.Add(day);
                }

                string format = "dd/MM/yyyy";
                System.Diagnostics.Debug.WriteLine($"RES blockedDates: {string.Join(',', _blockedDatesSet.Select(d => d.ToString(format)))}");

                SelectableDayPredicate = date => !_blockedDatesSet.Contains(date.Date);

                OnPropertyChanged(nameof(SelectableDayPredicate));

            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les dates non disponibles pour les congés.";
                System.Diagnostics.Debug.WriteLine($"Error LoadBlockedDatesAsync: {ex.Message}");
                _blockedDatesSet.Clear();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadLeaveTypesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                List<LeaveTypeItem> filtered = await _odooClient.GetLeaveTypesAsync(UseAllocation, SelectedRange?.StartDate!.Value.Year);

                if (UseAllocation)
                {
                    List<LeaveTypeItem> updatedList = new();
                    foreach (LeaveTypeItem leave in filtered)
                    {
                        (_, _, int remaining) = await _odooClient.GetNumberTimeOffAsync(
                            yearRequested: SelectedRange?.StartDate!.Value.Year,
                            idRequest: leave.Id);

                        updatedList.Add(leave with { Name = $"{leave.Name} ({remaining} restants)" });
                    }
                    filtered = updatedList;
                }
                LeaveTypes = new ObservableCollection<LeaveTypeItem>(filtered);
                SelectedLeaveType = LeaveTypes.FirstOrDefault();

                if (LeaveTypes.Count == 0)
                {
                    ErrorMessage = "Aucun type de congé disponible. Veuillez demander une allocation.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.";
                System.Diagnostics.Debug.WriteLine($"Error LoadLeaveTypesAsync: {ex.Message}");
                LeaveTypes = new ObservableCollection<LeaveTypeItem>();
                SelectedLeaveType = null;
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

                (int allocated, int taken, int total) = await _odooClient.GetNumberTimeOffAsync(SelectedRange?.StartDate?.Year);
                TotalAllocated = allocated;
                TotalTaken = taken;
                TotalRemaining = total;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de rafraîchir les totaux de congés.";
                System.Diagnostics.Debug.WriteLine($"Error RefreshTotalsAsync: {ex.Message}");
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
            try
            {
                IsBusy = true;

                int createdId = await _odooClient.CreateLeaveRequestAsync(
                    leaveTypeId: SelectedLeaveType!.Id,
                    startDate: SelectedRange!.StartDate!.Value,
                    endDate: SelectedRange!.EndDate!.Value,
                    reason: Reason?.Trim() ?? string.Empty
                );

                return (true, createdId, $"Votre demande de congé a été créée dans Odoo (id = {createdId}).");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Res SubmitAsync: {SelectedRange!.StartDate!.Value} - {SelectedRange!.EndDate!.Value}");
                System.Diagnostics.Debug.WriteLine($"Error SubmitAsync: {ex.Message}");
                return (false, null, "Impossible d'envoyer la demande de congé.");
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

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}