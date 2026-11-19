namespace C3dMcpBridge;

/// <summary>The only file that uses Windows Forms (the type-check project in tools/typecheck swaps in a stub).</summary>
internal static class Ui
{
    private sealed class Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public Owner(IntPtr h) { Handle = h; }
    }

    /// <summary>Modal Yes/No on Civil 3D's main window; "No" is the default button.</summary>
    public static bool AskYesNo(string title, string text) =>
        System.Windows.Forms.MessageBox.Show(new Owner(AcApp.MainWindow.Handle), text, title,
            System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Warning,
            System.Windows.Forms.MessageBoxDefaultButton.Button2) == System.Windows.Forms.DialogResult.Yes;
}
