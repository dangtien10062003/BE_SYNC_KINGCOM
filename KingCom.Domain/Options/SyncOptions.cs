namespace KingCom.Domain.Options;

public sealed record SyncOptions
{
    public string LogFile { get; init; } = "logs/stock-sync.jsonl";
    public bool AutoEnabled { get; init; } = false;
    public int AutoIntervalMinutes { get; init; } = 30;
}
