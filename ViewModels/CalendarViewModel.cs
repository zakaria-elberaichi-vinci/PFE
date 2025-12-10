using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PFE.Helpers;
using PFE.Models;
using PFE.Services;
using Syncfusion.Maui.Scheduler;

namespace PFE.ViewModels
{
    public class CalendarViewModel : INotifyPropertyChanged
    {
        private readonly OdooClient _odooClient;
        private bool _isBusy;
        private string _errorMessage = string.Empty;
        private int _selectedYear;

        private int _totalAllocated;
        private int _totalTaken;
        private int _totalRemaining;
        private DateTime? _selectedDate;

        public event PropertyChangedEventHandler? PropertyChanged;

        public CalendarViewModel(OdooClient odooClient)
        {
            _odooClient = odooClient;
            _selectedYear = DateTime.Today.Year;
            Appointments = new ObservableCollection<SchedulerAppointment>();

            RefreshCommand = new RelayCommand(async _ => await LoadAsync(), _ => !IsBusy);
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (_selectedYear != value)
                {
                    _selectedYear = value;
                    OnPropertyChanged();
                    _ = LoadAsync();
                }
            }
        }

        public int TotalAllocated
        {
            get => _totalAllocated;
            private set { _totalAllocated = value; OnPropertyChanged(); }
        }

        public int TotalTaken
        {
            get => _totalTaken;
            private set { _totalTaken = value; OnPropertyChanged(); }
        }

        public int TotalRemaining
        {
            get => _totalRemaining;
            private set { _totalRemaining = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SchedulerAppointment> Appointments { get; }

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

        public DateTime? SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (_selectedDate != value)
                {
                    _selectedDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEmployee => _odooClient.session.Current.IsAuthenticated && !_odooClient.session.Current.IsManager;

        public ICommand RefreshCommand { get; }

        public async Task LoadAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                if (!IsEmployee)
                {
                    ErrorMessage = "Veuillez vous connecter en tant qu'employé.";
                    TotalAllocated = 0;
                    TotalTaken = 0;
                    TotalRemaining = 0;
                    Appointments.Clear();
                    return;
                }

                List<AllocationSummary> summaries = await _odooClient.GetAllocationsSummaryAsync(SelectedYear);

                TotalAllocated = summaries.Sum(s => s.TotalAllocated);
                TotalTaken = summaries.Sum(s => s.TotalTaken);
                TotalRemaining = summaries.Sum(s => s.TotalRemaining);

                List<Leave> leaves = await _odooClient.GetLeavesAsync(SelectedYear);

                Appointments.Clear();

                foreach (Leave leave in leaves)
                {
                    string colorHex = LeaveTypeHelper.GetColorHex(leave.Type);
                    Brush background = new SolidColorBrush(Color.FromArgb(colorHex));

                    Appointments.Add(new SchedulerAppointment
                    {
                        Subject = $"{leave.Type} : {leave.Days} {(leave.Days > 1 ? "jours" : "jour")}",
                        StartTime = leave.StartDate,
                        EndTime = leave.EndDate,
                        IsAllDay = true,
                        Background = background,
                        Notes = leave.Status,
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Impossible de charger les données : {ex.Message}";
                TotalAllocated = 0;
                TotalTaken = 0;
                TotalRemaining = 0;
                Appointments.Clear();
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmployee));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
