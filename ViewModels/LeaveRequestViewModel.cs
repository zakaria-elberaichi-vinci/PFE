using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Models;
using PFE.Services;
using Syncfusion.Maui.Calendar;
using static PFE.Helpers.DateHelper;

namespace PFE.ViewModels
{
    public class LeaveRequestViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;

        private CalendarDateRange? _selectedRange;

        private ObservableCollection<LeaveTypeItem> _leaveTypes = [];

        private readonly HashSet<DateTime> _blockedDatesSet = [];

        private LeaveTypeItem? _selectedLeaveType;

        private string _reason = string.Empty;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private string _validationMessage = string.Empty;
        private bool _isAccessDenied;
        private bool _hasOverlap;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LeaveRequestViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;

            SelectedRange = new CalendarDateRange(DateTime.Today, null);

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
                {
                    OnPropertyChanged(nameof(LeaveTypes));
                }
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
                    UpdateValidationMessage();
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Reason
        {
            get => _reason;
            set
            {
                if (Set(ref _reason, value))
                {
                    OnPropertyChanged(nameof(Reason));
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
                {
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            private set
            {
                if (Set(ref _validationMessage, value))
                {
                    OnPropertyChanged(nameof(ValidationMessage));
                }
            }
        }

        public bool HasOverlap
        {
            get => _hasOverlap;
            private set
            {
                if (Set(ref _hasOverlap, value))
                {
                    OnPropertyChanged(nameof(HasOverlap));
                    OnPropertyChanged(nameof(CanSubmit));
                    (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
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
                {
                    OnPropertyChanged(nameof(IsAccessDenied));
                }
            }
        }

        public bool AreDatesValid => SelectedRange?.StartDate != null && SelectedRange?.EndDate != null;

        public bool CanSubmit =>
            !IsBusy &&
            AreDatesValid &&
            SelectedLeaveType is not null &&
            !HasOverlap &&
            !(SelectedLeaveType.RequiresAllocation && (SelectedLeaveType.Days ?? 0) <= 0);

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
            await LoadBlockedDatesAsync();
        }

        private async Task OnDatesChangedAsync()
        {
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
            ValidationMessage = string.Empty;
            HasOverlap = false;

            await LoadLeaveTypesAsync();
            await CheckOverlapAsync();

            UpdateValidationMessage();
            OnPropertyChanged(nameof(CanSubmit));
            (SubmitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task CheckOverlapAsync()
        {
            try
            {
                HasOverlap = await _odooClient.HasOverlappingLeaveAsync(
                    SelectedRange!.StartDate!.Value,
                    SelectedRange!.EndDate!.Value
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error CheckOverlapAsync: {ex.Message}");
                HasOverlap = false;
            }
        }

        private void UpdateValidationMessage()
        {
            ValidationMessage = SelectedLeaveType?.RequiresAllocation == true && (SelectedLeaveType.Days ?? 0) <= 0
                ? "Ce type de congé nécessite une allocation, mais vous n'avez plus de jours disponibles."
                : HasOverlap ? "Les dates sélectionnées chevauchent un congé existant." : string.Empty;
        }

        private async Task LoadBlockedDatesAsync()
        {
            try
            {
                IsBusy = true;
                ErrorMessage = string.Empty;

                List<Leave> leaves = await _odooClient.GetLeavesAsync("confirm", "validate", "validate1");

                _blockedDatesSet.Clear();

                foreach (Leave leave in leaves)
                {
                    DateTime start = leave.StartDate.Date;
                    DateTime end = leave.EndDate.Date;

                    foreach (DateTime day in ExpandRangeToDays(start, end))
                    {
                        _ = _blockedDatesSet.Add(day);
                    }
                }

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

                List<LeaveTypeItem> typesWithoutAllocation = await _odooClient.GetLeaveTypesAsync();

                List<AllocationSummary> allocations = await _odooClient.GetAllocationsSummaryAsync();

                List<LeaveTypeItem> typesWithAllocation = allocations
                    .Select(a => new LeaveTypeItem(
                        Id: a.LeaveTypeId,
                        Name: $"{a.LeaveTypeName} ({a.TotalRemaining} restants sur {a.TotalAllocated})",
                        RequiresAllocation: true,
                        Days: a.TotalRemaining
                    ))
                    .ToList();

                List<LeaveTypeItem> combinedTypes = typesWithAllocation
                    .Concat(typesWithoutAllocation)
                    .OrderBy(t => t.Name)
                    .ThenBy(t => t.Id)
                    .ToList();

                LeaveTypes = new ObservableCollection<LeaveTypeItem>(combinedTypes);

                SelectedLeaveType ??= LeaveTypes.FirstOrDefault();

                UpdateValidationMessage();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Impossible de charger les types de congés disponibles.";
                System.Diagnostics.Debug.WriteLine($"Error LoadLeaveTypesAsync: {ex.Message}");
                LeaveTypes = [];
                SelectedLeaveType = null;
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
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}