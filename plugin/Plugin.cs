using AcRt = Autodesk.AutoCAD.Runtime;

[assembly: AcRt.ExtensionApplication(typeof(C3dMcpBridge.Plugin))]
[assembly: AcRt.CommandClass(typeof(C3dMcpBridge.Commands))]

namespace C3dMcpBridge;

/// <summary>
/// Entry point. Civil 3D calls Initialize on its main thread when the DLL is loaded
/// (autoload bundle or NETLOAD) and Terminate on shutdown.
/// </summary>
public class Plugin : AcRt.IExtensionApplication
{
    public void Initialize()
    {
        // Capture the document manager here, on the main thread; the socket threads need it later.
        MainThread.Init(AcApp.DocumentManager);
        try
        {
            BridgeServer.Start();
            Log(BridgeServer.Describe());
        }
        catch (Exception ex)
        {
            Log("Civil 3D MCP bridge failed to start: " + ex.Message);
        }
    }

    public void Terminate() => BridgeServer.Stop();

    internal static void Log(string message)
    {
        // MdiActiveDocument is null while Civil 3D is still starting up with no drawing open.
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + message + "\n");
    }
}

public class Commands
{
    /// <summary>Shows the bridge state and selects this Civil 3D instance for MCP calls.</summary>
    [AcRt.CommandMethod("MCPSTATUS")]
    public void Status()
    {
        BridgeServer.WriteBridgeFile();
        Plugin.Log(BridgeServer.Describe());
    }

    /// <summary>Allow MCP edits in the active drawing without the confirmation dialog.</summary>
    [AcRt.CommandMethod("MCPALLOW")]
    public void Allow()
    {
        WriteGate.Grant(ActiveDoc(), "MCPALLOW command");
        Plugin.Log("MCP clients may now edit the active drawing. Run MCPREVOKE to stop.");
    }

    /// <summary>Withdraw the edit permission; the next edit request asks again.</summary>
    [AcRt.CommandMethod("MCPREVOKE")]
    public void Revoke()
    {
        WriteGate.Revoke("MCPREVOKE command");
        Plugin.Log("MCP editing is disabled. Reading still works.");
    }
}
