using System.Windows.Input;

namespace PFE.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsyncDelegate;
        private readonly Predicate<object?>? _canExecutePredicate;
        public event EventHandler? _canExecuteChanged;

        public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
        {
            _executeAsyncDelegate = executeAsync;
            _canExecutePredicate = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => _canExecuteChanged += value; remove => _canExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecutePredicate == null || _canExecutePredicate(parameter);
        }

        public async void Execute(object? parameter)
        {
            await _executeAsyncDelegate(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            EventHandler? handler = _canExecuteChanged;
            handler?.Invoke(this, EventArgs.Empty);
        }

    }
}
