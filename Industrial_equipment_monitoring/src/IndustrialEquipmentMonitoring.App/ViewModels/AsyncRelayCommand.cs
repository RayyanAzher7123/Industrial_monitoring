using System.Windows.Input;

namespace IndustrialEquipmentMonitoring.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool> _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _busy;

    public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_busy && _canExecute();
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _busy = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            _busy = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
