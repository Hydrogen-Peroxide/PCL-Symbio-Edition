using System.Windows;
using System.Windows.Controls;

namespace PCL;

internal static class DialogPresentation
{
    internal static void Show(FrameworkElement dialog)
    {
        var window = ModMain.frmMain;
        window.DialogLayer.Visibility = Visibility.Visible;
        SymbioMotion.Fade(window.PanMsgBackground, 1);
        SymbioMotion.EnterDepth(dialog, 18, .94, SymbioMotion.Pop);
    }

    internal static void Close(FrameworkElement dialog)
    {
        dialog.IsHitTestVisible = false;
        var window = ModMain.frmMain;
        if (ModMain.WaitingMyMsgBox.Count == 0) SymbioMotion.Fade(window.PanMsgBackground, 0);
        SymbioMotion.ExitDepth(dialog, 10, .96, () =>
        {
            if (dialog.Parent is Panel host) host.Children.Remove(dialog);
        });
    }
}
