namespace KingCom.Domain.Options;

public sealed record SyncOptions
{
    public string LogFile { get; init; } = "logs/stock-sync.jsonl";
}
