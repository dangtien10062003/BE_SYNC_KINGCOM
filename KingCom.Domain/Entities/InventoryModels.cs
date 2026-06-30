namespace KingCom.Domain.Entities;

public sealed record StockRow(string Sku, int HcmStock, int HnStock, string ProductName = "");

public sealed record InventoryUpdateLine(string Sku, string LocationId, long ProductId, long ProductVariantId, int Quantity);
