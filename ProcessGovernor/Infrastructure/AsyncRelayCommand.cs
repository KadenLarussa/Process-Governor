using System.Windows.Input;

namespace ProcessGovernor.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowsConcurrentExecutions;
    private CancellationTokenSource? _executionCancellation;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this((_, _) => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
        : this((_, token) => execute(token), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<object?, CancellationToken, Task> execute, Predicate<object?>? canExecute = null, bool allowsConcurrentExecutions = false)
    {
        _execute = execute;
        _canExecute = canExecute;
        _allowsConcurrentExecutions = allowsConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting == value)
            {
                return;
            }

            _isExecuting = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter)
    {
        if (!_allowsConcurrentExecutions && IsExecuting)
        {
            return false;
        }

        return _canExecute?.Invoke(parameter) ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _executionCancellation = new CancellationTokenSource();
        try
        {
            IsExecuting = true;
            await _execute(parameter, _executionCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _executionCancellation.Dispose();
            _executionCancellation = null;
            IsExecuting = false;
        }
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
