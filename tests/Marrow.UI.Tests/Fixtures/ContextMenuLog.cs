using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A context-menu presenter that writes "present" to a shared log, to order it against the menu's events.</summary>
public sealed class ContextMenuLog(List<string> log) : IContextMenuPresenter
{
    /// <inheritdoc />
    public void Present(Control target, Point at, IReadOnlyList<ContextMenuEntry> entries) => log.Add("present");
}

/// <summary>A command that is always enabled and runs an action.</summary>
public sealed class CountingCommand(Action run) : System.Windows.Input.ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public void Execute(object? parameter) => run();
}
