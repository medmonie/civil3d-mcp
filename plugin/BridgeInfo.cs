namespace C3dMcpBridge;

/// <summary>Version and per-user data folder of the bridge.</summary>
internal static class BridgeInfo
{
    /// <summary>Keep PackageContents.xml AppVersion in step; the tests check it.</summary>
    public const string Version = "0.24.0";

    /// <summary>%LOCALAPPDATA%\C3dMcpBridge: bridge.json (connection details) and audit.log.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C3dMcpBridge");
}
