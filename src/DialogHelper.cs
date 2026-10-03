using System;
using System.Windows;

namespace IPChanger;

internal static class DialogHelper
{
    public static void ShowError(Window? owner, string message, string title)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowWarning(Window? owner, string message, string title)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static void ShowInfo(Window? owner, string message, string title)
    {
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static bool ShowQuestion(Window? owner, string message, string title)
    {
        var result = MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}