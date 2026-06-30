namespace KingCom.Domain.Options;

public sealed record HaravanOptions
{
    public string ApiBaseUrl { get; init; } = "";
    public string ApiVersion { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string? LocationHcmId { get; init; }
    public string? LocationHnId { get; init; }
    public int BatchSize { get; init; } = 100;
    public bool BlockWrites { get; init; } = true;
    public string Reason { get; init; } = "";
    public string Note { get; init; } = "";
}
