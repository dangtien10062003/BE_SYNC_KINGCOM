using KingCom.Domain.Entities;

namespace KingCom.Application.Contracts;

public sealed record AnalyzeResult(List<AnalyzeItem> Items, int HaravanVariantCount);
public sealed record SyncResult(bool DryRun, int Total, int Success, int Failed, List<AnalyzeItem> Items, string Message);
public sealed record BackorderResult(int Total, List<BackorderItem> Items);
public sealed record BackorderUpdateRequest(List<long> VariantIds, bool Enabled);
public sealed record BackorderUpdateResult(int Success, int Failed, List<BackorderUpdateFailure> Failures);
public sealed record BackorderUpdateFailure(long VariantId, string Message);

public sealed record BackorderItem(
    int Stt,
    long ProductId,
    string ProductTitle,
    long VariantId,
    string VariantTitle,
    string Sku,
    string InventoryPolicy,
    bool Enabled,
    int HcmStock,
    int HnStock,
    int TotalStock,
    string Message);

public sealed record AnalyzeItem(
    string Id,
    int RowNumber,
    string Sku,
    string NormalizedSku,
    string ProductName,
    int HcmStock,
    int HnStock,
    int? HaravanHcmStock,
    int? HaravanHnStock,
    HaravanVariant? HaravanVariant,
    string Status,
    string StatusLabel,
    string Message,
    string[] Errors,
    int TotalChange)
{
    public static AnalyzeItem NotFound(StockRow row) => new(
        Id: $"{row.Sku}-not-found",
        RowNumber: 0,
        Sku: row.Sku,
        NormalizedSku: row.Sku.Trim().ToUpperInvariant(),
        ProductName: row.ProductName,
        HcmStock: row.HcmStock,
        HnStock: row.HnStock,
        HaravanHcmStock: null,
        HaravanHnStock: null,
        HaravanVariant: null,
        Status: "not_found",
        StatusLabel: "HRV khong co ma",
        Message: "HRV khong co ma nay",
        Errors: [],
        TotalChange: row.HcmStock + row.HnStock);
}

public sealed record SyncRequest(bool DryRun = true);
