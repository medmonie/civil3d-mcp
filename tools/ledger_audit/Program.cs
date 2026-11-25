global using System.Text.Json;
global using static C3dMcpBridge.AuditStubs;

namespace C3dMcpBridge;

internal readonly record struct DrawingTarget(Guid SessionId, Guid DrawingId)
{
    public static DrawingTarget Read(JsonElement p) => new(p.GetProperty("session").GetGuid(), p.GetProperty("drawing").GetGuid());
    public static Guid ReadId(JsonElement p, string name) => p.GetProperty(name).GetGuid();
}

internal sealed class CadQueueTimeoutException(string message) : TimeoutException(message);

internal static class Json
{
    public static readonly JsonSerializerOptions Out = new();
}

internal static class AuditStubs
{
    public static bool? BoolParam(JsonElement p, string name) => p.TryGetProperty(name, out var v) ? v.GetBoolean() : null;
    public static string? StrParam(JsonElement p, string name) => p.TryGetProperty(name, out var v) ? v.GetString() : null;
    public static string ErrorText(Exception ex) => ex.Message;
}

internal static class Program
{
    private static async Task Main()
    {
        var target = new DrawingTarget(Guid.NewGuid(), Guid.NewGuid());
        var args = JsonSerializer.SerializeToElement(new { workflow = "audit" });
        var first = Guid.NewGuid();
        var uncertain = Guid.NewGuid();
        var queueTimeout = Guid.NewGuid();
        int executions = 0;
        Task<object> Commit()
        {
            executions++;
            return Task.FromResult<object>(new { applied = true });
        }
        JsonElement Status(Guid id) => JsonSerializer.SerializeToElement(EditRequests.Status(JsonSerializer.SerializeToElement(
            new { session = target.SessionId, drawing = target.DrawingId, request_id = id })));

        await EditRequests.RunAsync(first, target, args, Commit);
        try
        {
            await EditRequests.RunAsync(uncertain, target, args, () => throw new InvalidOperationException("outcome unknown"));
            throw new Exception("Expected an unknown-outcome error.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "outcome unknown") { }
        await EditRequests.RunAsync(uncertain, target, args, Commit);
        if (Status(uncertain).GetProperty("retryable").GetBoolean() || executions != 1)
            throw new Exception("An uncertain operation was allowed to repeat.");
        try
        {
            await EditRequests.RunAsync(queueTimeout, target, args, () => throw new CadQueueTimeoutException("never started"));
            throw new Exception("Expected a pre-start timeout.");
        }
        catch (CadQueueTimeoutException) { }
        if (!Status(queueTimeout).GetProperty("retryable").GetBoolean())
            throw new Exception("The pre-start timeout was marked terminal.");
        await EditRequests.RunAsync(queueTimeout, target, args, Commit);
        for (int i = 3; i < 8192; i++)
            await EditRequests.RunAsync(Guid.NewGuid(), target, args, Commit);
        try
        {
            await EditRequests.RunAsync(Guid.NewGuid(), target, args, Commit);
            throw new Exception("An edit was accepted after the ledger filled.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("history")) { }
        await EditRequests.RunAsync(first, target, args, Commit);
        if (executions != 8191 || Status(first).GetProperty("state").GetString() != "completed")
            throw new Exception("The oldest ID did not retain its replay protection.");
        Console.WriteLine("Actual EditRequests.cs passed capacity, first-ID replay, pre-start retry, and uncertain-outcome tests.");
    }
}
