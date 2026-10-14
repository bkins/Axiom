using Avalonia.Controls;
using Avalonia.Input;

namespace Axiom.App;

public partial class MainWindow
{
    private bool _altPressedAlone;

    private bool TryHandleCommandMenuKey(KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Key.LeftAlt or Key.RightAlt)
        {
            _altPressedAlone = eventArgs.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt;
            return false;
        }
        _altPressedAlone = false;
        if (eventArgs.Key == Key.F10 && eventArgs.KeyModifiers == KeyModifiers.None)
        {
            FileCommandsButton.Focus(NavigationMethod.Tab);
            eventArgs.Handled = true;
            return true;
        }
        if (eventArgs.Key == Key.F5 && eventArgs.KeyModifiers == KeyModifiers.None)
        {
            RefreshWorkspace_OnClick(this, new Avalonia.Interactivity.RoutedEventArgs());
            eventArgs.Handled = true;
            return true;
        }
        if (eventArgs.KeyModifiers != KeyModifiers.Alt) return false;
        string? section = eventArgs.Key switch
        {
            Key.F => "File"
          , Key.E => "Edit"
          , Key.M => "Format"
          , _     => null
        };
        if (section is null) return false;
        FileCommandsButton.Focus(NavigationMethod.Tab);
        FileCommandsButton.Flyout?.ShowAt(FileCommandsButton);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (FileCommandsButton.Flyout is MenuFlyout menu && menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Header?.ToString() == section) is { } group)
            {
                group.Focus(NavigationMethod.Tab);
                group.IsSubMenuOpen = true;
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        eventArgs.Handled = true;
        return true;
    }

    private async void Window_OnKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Key.LeftCtrl or Key.RightCtrl)
            await QueueTabCycleAsync(null);
        if (eventArgs.Key is not (Key.LeftAlt or Key.RightAlt)) return;
        if (_altPressedAlone)
        {
            FileCommandsButton.Focus(NavigationMethod.Tab);
            eventArgs.Handled = true;
        }
        _altPressedAlone = false;
    }
}
