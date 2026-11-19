namespace C3dMcpBridge;

/// <summary>Stand-in for plugin/Ui.cs so the type-check needs no Windows Forms. Never shipped.</summary>
internal static class Ui
{
    public static bool AskYesNo(string title, string text) => false;
}
