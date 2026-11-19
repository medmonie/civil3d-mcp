namespace C3dMcpBridge;

/// <summary>Append-only record of every permission decision and every edit batch. Best effort: never blocks work.</summary>
internal static class Audit
{
    private const long MaxBytes = 5 * 1024 * 1024;

    public static string FilePath => Path.Combine(BridgeInfo.DataFolder, "audit.log");

    public static void Write(string action, object data)
    {
        try
        {
            Directory.CreateDirectory(BridgeInfo.DataFolder);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > MaxBytes)
                File.Move(FilePath, Path.ChangeExtension(FilePath, ".1.log"), overwrite: true);   // keep one previous file
            File.AppendAllText(FilePath, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, action, data }, Json.Out) + "\n");
        }
        catch { /* audit must never break an operation */ }
    }
}

/// <summary>JSON output settings shared by the bridge reply and the audit log.</summary>
internal static class Json
{
    // Civil 3D returns NaN/Infinity for some values (slope of a vertical pipe, cover without a surface);
    // the default serializer throws on those, so write them as the strings "NaN"/"Infinity".
    public static readonly JsonSerializerOptions Out = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
}
