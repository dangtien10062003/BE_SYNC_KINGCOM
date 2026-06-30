namespace KingCom.Domain.Options;

public sealed record AuthOptions
{
    public bool Enabled { get; init; }
}

public sealed record JwtOptions
{
    public string Secret { get; init; } = "";
    public string Issuer { get; init; } = "KingComDongBo";
    public string Audience { get; init; } = "KingComDongBo";
    public int ExpireMinutes { get; init; } = 480;
}
