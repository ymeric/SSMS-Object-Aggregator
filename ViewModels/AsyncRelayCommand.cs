#pragma warning disable VSTHRD100 // async void is required by ICommand.Execute(object) — exceptions are caught inside
#pragma warning disable VSTHRD001 // Dispatcher.Invoke with CheckAccess() guard is the correct WPF pattern here
using System.Windows;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.ViewModels;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Object Aggregator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
        {
            return false;
        }

        if (_canExecute is null)
        {
            return true;
        }

        if (parameter is T typed)
        {
            return _canExecute(typed);
        }

        return _canExecute(default);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            if (parameter is T typed)
            {
                await _execute(typed).ConfigureAwait(true);
            }
            else
            {
                await _execute(default).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Object Aggregator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
            return;
        }

        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}