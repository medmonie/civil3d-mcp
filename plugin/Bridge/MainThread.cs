using Autodesk.AutoCAD.ApplicationServices;

namespace C3dMcpBridge;

/// <summary>
/// The Civil 3D / AutoCAD API is not thread-safe, and in 2027 even touching DocumentCollection from another
/// thread throws "The calling thread cannot access this object because a different thread owns it".
/// So socket threads never touch the API: they Post to Civil 3D's UI synchronization context (captured on the
/// main thread in Initialize), and only from there call ExecuteInApplicationContext to run the handler.
/// </summary>
internal static class MainThread
{
    private static DocumentCollection? _docs;
    private static SynchronizationContext? _ui;

    public static bool HasUiContext => _ui != null;

    /// <summary>Must be called on Civil 3D's main thread (from IExtensionApplication.Initialize).</summary>
    public static void Init(DocumentCollection docs)
    {
        _docs = docs;
        _ui = SynchronizationContext.Current;
    }

    public static async Task<T> RunAsync<T>(Func<T> work, TimeSpan timeout)
    {
        var docs = _docs ?? throw new InvalidOperationException("Bridge is not initialised.");
        var ui = _ui ?? throw new InvalidOperationException(
            "No UI synchronization context was captured at start-up; the bridge cannot reach Civil 3D's main thread.");
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 0 = queued, 1 = running, 2 = abandoned (caller timed out before Civil 3D picked it up).
        int state = 0;

        ui.Post(_ =>
        {
            // Now on Civil 3D's main thread: safe to use the document collection.
            try
            {
                docs.ExecuteInApplicationContext(__ =>
                {
                    // If we timed out while queued, do nothing. This matters most for edits:
                    // a request the caller already gave up on must never modify the drawing later.
                    if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                    try { tcs.TrySetResult(work()); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                }, null!);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);

        try
        {
            return await tcs.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
                throw new TimeoutException(
                    $"Civil 3D did not become available within {timeout.TotalSeconds:0}s " +
                    "(a command, dialog or long regen is probably active). Request cancelled.");

            // Already running: let it finish rather than abandon a half-done operation.
            return await tcs.Task;
        }
    }
}
