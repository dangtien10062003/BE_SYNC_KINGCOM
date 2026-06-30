namespace KingCom.Domain.Options;

public sealed record InventoryOptions
{
    public string StockQuery { get; init; } = "";
}
