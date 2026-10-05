using System.Windows.Input;

namespace VdHelper.Core.Mvvm;

/// <summary>Minimal async command. Enough for button handlers; no framework needed.</summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    /// <summary>Set while a run is in flight so callers can await it instead of guessing.</summary>
    public Task? ExecutionTask { get; private set; }

    /// <summary>Raised when a run throws. Without this an <c>async void</c> failure would tear
    /// down the whole app mid-session, taking the user's evidence with it.</summary>
    public event Action<Exception>? Failed;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        ExecutionTask = RunAsync();
        await ExecutionTask;
    }

    private async Task RunAsync()
    {
        try { await execute(); }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
/// <summary>Synchronous counterpart; the symptom chips need nothing asynchronous.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
