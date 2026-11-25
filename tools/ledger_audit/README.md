# Ledger audit

Offline check of `plugin/Bridge/EditRequests.cs` (no Civil 3D needed): capacity limit, replay of the first request ID,
retry after a queue timeout, and refusal to retry an edit whose outcome is uncertain.

    cd tools/ledger_audit
    dotnet run

Needs the .NET 10 SDK. From the ChatGPT protocol audit (docs/MCP_PROTOCOL_AUDIT.log).
