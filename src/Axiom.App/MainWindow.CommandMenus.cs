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
        Button? button = eventArgs.Key switch
        {
            Key.F => FileCommandsButton
          , Key.E => EditCommandsButton
          , Key.M => FormatCommandsButton
          , _     => null
        };
        if (button is null) return false;
        button.Focus(NavigationMethod.Tab);
        button.Flyout?.ShowAt(button);
        eventArgs.Handled = true;
        return true;
    }

    private void Window_OnKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Key.LeftAlt or Key.RightAlt)) return;
        if (_altPressedAlone)
        {
            FileCommandsButton.Focus(NavigationMethod.Tab);
            eventArgs.Handled = true;
        }
        _altPressedAlone = false;
    }
}
