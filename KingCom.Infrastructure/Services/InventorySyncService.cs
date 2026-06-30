using System.Text.Json;
using KingCom.Application.Contracts;
using KingCom.Domain.Entities;
using KingCom.Domain.Options;
using KingCom.Infrastructure.Integrations;
using KingCom.Infrastructure.Inventory;
using KingCom.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;

namespace KingCom.Infrastructure.Services;

public sealed class InventorySyncService(InventoryReader reader, HaravanClient haravan, JsonlSyncLogger logger, IConfiguration config, InventorySyncGate gate)
{
    private readonly HaravanOptions _options = config.GetSection("Haravan").Get<HaravanOptions>() ?? new HaravanOptions();

    public async Task<AnalyzeResult> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var stockRows = await reader.ReadAsync(cancellationToken);
        var variants = await haravan.FetchVariantsAsync(cancellationToken);
        var variantsWithSku = variants.Where(variant => !string.IsNullOrWhiteSpace(variant.Sku)).ToList();
        var variantsMissingSku = variants.Where(variant => string.IsNullOrWhiteSpace(variant.Sku)).ToList();
        var variantBySku = variantsWithSku
            .GroupBy(variant => variant.Sku.Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First());

        var stockSkuSet = stockRows
            .Select(row => row.Sku.Trim().ToUpperInvariant())
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .ToHashSet();

        var locationIds = GetLocationIds();
        var balances = await haravan.FetchBalancesAsync(variants.Select(variant => variant.Id), locationIds, cancellationToken);
        var items = stockRows.Select(row => BuildAnalyzeItem(row, variantBySku, balances)).ToList();
        items.AddRange(variantBySku
            .Where(pair => !stockSkuSet.Contains(pair.Key))
            .Select(pair => BuildTedMissingItem(pair.Value, balances)));
        items.AddRange(variantsMissingSku.Select(variant => BuildHaravanMissingSkuItem(variant, balances)));

        logger.Write("analyze_finished", new
        {
            dbTotal = stockRows.Count,
            total = items.Count,
            needUpdate = items.Count(item => item.Status == "matched_update"),
            hrvNotFound = items.Count(item => item.Status == "not_found"),
            tedNotFound = items.Count(item => item.Status == "ted_not_found"),
            hrvMissingSku = items.Count(item => item.Status == "haravan_missing_sku")
        });

        return new AnalyzeResult(items, variants.Count);
    }

    public async Task<BackorderResult> FindBackorderEnabledAsync(CancellationToken cancellationToken = default)
    {
        var variants = await haravan.FetchVariantsAsync(cancellationToken);
        var locationIds = GetLocationIds();
        var balances = await haravan.FetchBalancesAsync(variants.Select(variant => variant.Id), locationIds, cancellationToken);

        var items = variants
            .Select((variant, index) =>
            {
                var byLocation = balances.GetValueOrDefault(variant.Id.ToString()) ?? new Dictionary<string, int>();
                var hcmStock = byLocation.GetValueOrDefault(_options.LocationHcmId ?? "", 0);
                var hnStock = byLocation.GetValueOrDefault(_options.LocationHnId ?? "", 0);
                var enabled = IsBackorderEnabled(variant);
                return new BackorderItem(
                    Stt: index + 1,
                    ProductId: variant.ProductId,
                    ProductTitle: variant.ProductTitle ?? "",
                    VariantId: variant.Id,
                    VariantTitle: variant.VariantTitle ?? "",
                    Sku: variant.Sku,
                    InventoryPolicy: FormatInventoryPolicy(variant.InventoryPolicy),
                    Enabled: enabled,
                    HcmStock: hcmStock,
                    HnStock: hnStock,
                    TotalStock: hcmStock + hnStock,
                    Message: enabled ? "Dang bat dat hang khi het hang" : "Dang tat dat hang khi het hang");
            })
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.Sku)
            .ToList();

        logger.Write("backorder_check_finished", new { total = items.Count, enabled = items.Count(item => item.Enabled) });
        return new BackorderResult(items.Count, items);
    }

    public async Task<BackorderUpdateResult> SetBackorderPolicyAsync(BackorderUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var ids = request.VariantIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new BackorderUpdateResult(0, 0, []);

        var failed = new List<BackorderUpdateFailure>();
        var success = 0;
        foreach (var id in ids)
        {
            try
            {
                await haravan.SetVariantInventoryPolicyAsync(id, request.Enabled, cancellationToken);
                success += 1;
            }
            catch (Exception ex)
            {
                failed.Add(new BackorderUpdateFailure(id, ex.Message));
            }
        }

        logger.Write("backorder_policy_updated", new
        {
            enabled = request.Enabled,
            total = ids.Count,
            success,
            failed = failed.Count
        });

        return new BackorderUpdateResult(success, failed.Count, failed);
    }

    public async Task<SyncResult> SyncAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        return await gate.RunExclusiveAsync(() => SyncCoreAsync(dryRun, cancellationToken), cancellationToken);
    }

    private async Task<SyncResult> SyncCoreAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var analyze = await AnalyzeAsync(cancellationToken);
        var targets = analyze.Items.Where(item => item.Status == "matched_update").ToList();

        if (dryRun)
        {
            logger.Write("dry_run_finished", new { total = targets.Count });
            return new SyncResult(true, targets.Count, 0, 0, analyze.Items, "Dry-run hoan tat, chua ghi Haravan.");
        }

        if (_options.BlockWrites)
        {
            logger.Write("sync_blocked", new { reason = "Haravan:BlockWrites=true" });
            throw new InvalidOperationException("Dang bat Haravan:BlockWrites=true, khong duoc ghi Haravan.");
        }

        var updates = BuildUpdates(targets);
        var success = 0;
        var failed = 0;

        foreach (var group in updates.GroupBy(update => update.LocationId))
        {
            foreach (var batch in group.ToList().Chunk(Math.Max(1, _options.BatchSize)))
            {
                try
                {
                    await haravan.SetInventoryAsync(group.Key, batch, cancellationToken);
                    success += batch.Length;
                    logger.Write("batch_success", new { locationId = group.Key, count = batch.Length, skus = batch.Select(x => x.Sku) });
                }
                catch (Exception ex)
                {
                    failed += batch.Length;
                    logger.Write("batch_failed", new { locationId = group.Key, count = batch.Length, error = ex.Message, skus = batch.Select(x => x.Sku) });
                }
            }
        }

        var resultItems = failed == 0
            ? analyze.Items.Select(item => item.Status == "matched_update"
                ? item with
                {
                    Status = "synced",
                    StatusLabel = "Da sync",
                    HaravanHcmStock = item.HcmStock,
                    HaravanHnStock = item.HnStock,
                    Message = "Dong bo thanh cong",
                    TotalChange = 0
                }
                : item).ToList()
            : analyze.Items;

        return new SyncResult(false, targets.Count, success, failed, resultItems, $"Sync xong: {success} thanh cong, {failed} that bai.");
    }

    private AnalyzeItem BuildAnalyzeItem(StockRow row, Dictionary<string, HaravanVariant> variants, Dictionary<string, Dictionary<string, int>> balances)
    {
        var normalizedSku = row.Sku.Trim().ToUpperInvariant();
        if (!variants.TryGetValue(normalizedSku, out var variant))
        {
            return AnalyzeItem.NotFound(row);
        }

        var byLocation = balances.GetValueOrDefault(variant.Id.ToString()) ?? new Dictionary<string, int>();
        var hcmStock = byLocation.GetValueOrDefault(_options.LocationHcmId ?? "", 0);
        var hnStock = byLocation.GetValueOrDefault(_options.LocationHnId ?? "", 0);
        var needUpdate = hcmStock != row.HcmStock || hnStock != row.HnStock;

        return new AnalyzeItem(
            Id: $"{row.Sku}-{variant.Id}",
            RowNumber: 0,
            Sku: row.Sku,
            NormalizedSku: normalizedSku,
            ProductName: !string.IsNullOrWhiteSpace(variant.ProductTitle) ? variant.ProductTitle : row.ProductName,
            HcmStock: row.HcmStock,
            HnStock: row.HnStock,
            HaravanHcmStock: hcmStock,
            HaravanHnStock: hnStock,
            HaravanVariant: variant,
            Status: needUpdate ? "matched_update" : "matched_same",
            StatusLabel: needUpdate ? "Can cap nhat" : "Da khop",
            Message: "",
            Errors: [],
            TotalChange: (row.HcmStock - hcmStock) + (row.HnStock - hnStock));
    }

    private AnalyzeItem BuildTedMissingItem(HaravanVariant variant, Dictionary<string, Dictionary<string, int>> balances)
    {
        var byLocation = balances.GetValueOrDefault(variant.Id.ToString()) ?? new Dictionary<string, int>();
        var hcmStock = byLocation.GetValueOrDefault(_options.LocationHcmId ?? "", 0);
        var hnStock = byLocation.GetValueOrDefault(_options.LocationHnId ?? "", 0);
        var normalizedSku = variant.Sku.Trim().ToUpperInvariant();

        return new AnalyzeItem(
            Id: $"{variant.Sku}-{variant.Id}-ted-not-found",
            RowNumber: 0,
            Sku: variant.Sku,
            NormalizedSku: normalizedSku,
            ProductName: FormatHaravanProductName(variant),
            HcmStock: 0,
            HnStock: 0,
            HaravanHcmStock: hcmStock,
            HaravanHnStock: hnStock,
            HaravanVariant: variant,
            Status: "ted_not_found",
            StatusLabel: "Ted khong co ma",
            Message: "Ted khong co ma nay",
            Errors: [],
            TotalChange: 0 - hcmStock - hnStock);
    }

    private AnalyzeItem BuildHaravanMissingSkuItem(HaravanVariant variant, Dictionary<string, Dictionary<string, int>> balances)
    {
        var byLocation = balances.GetValueOrDefault(variant.Id.ToString()) ?? new Dictionary<string, int>();
        var hcmStock = byLocation.GetValueOrDefault(_options.LocationHcmId ?? "", 0);
        var hnStock = byLocation.GetValueOrDefault(_options.LocationHnId ?? "", 0);

        return new AnalyzeItem(
            Id: $"missing-sku-{variant.Id}",
            RowNumber: 0,
            Sku: "",
            NormalizedSku: "",
            ProductName: variant.ProductTitle ?? "",
            HcmStock: 0,
            HnStock: 0,
            HaravanHcmStock: hcmStock,
            HaravanHnStock: hnStock,
            HaravanVariant: variant,
            Status: "haravan_missing_sku",
            StatusLabel: "Haravan thieu SKU",
            Message: $"Haravan chua dien SKU cho variant {variant.Id}",
            Errors: [],
            TotalChange: 0 - hcmStock - hnStock);
    }

    private List<InventoryUpdateLine> BuildUpdates(IEnumerable<AnalyzeItem> targets)
    {
        var updates = new List<InventoryUpdateLine>();
        foreach (var item in targets)
        {
            if (item.HaravanVariant is null) continue;
            updates.Add(new InventoryUpdateLine(item.Sku, _options.LocationHcmId ?? "", item.HaravanVariant.ProductId, item.HaravanVariant.Id, item.HcmStock));
            updates.Add(new InventoryUpdateLine(item.Sku, _options.LocationHnId ?? "", item.HaravanVariant.ProductId, item.HaravanVariant.Id, item.HnStock));
        }
        return updates.Where(update => !string.IsNullOrWhiteSpace(update.LocationId)).ToList();
    }

    private string[] GetLocationIds()
    {
        var ids = new[] { _options.LocationHcmId, _options.LocationHnId }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
        if (ids.Length < 2) throw new InvalidOperationException("Can cau hinh Haravan:LocationHcmId va Haravan:LocationHnId.");
        return ids;
    }

    private static string FormatHaravanProductName(HaravanVariant variant)
    {
        if (string.IsNullOrWhiteSpace(variant.VariantTitle) || variant.VariantTitle.Equals("Default Title", StringComparison.OrdinalIgnoreCase))
        {
            return variant.ProductTitle ?? "";
        }

        return string.IsNullOrWhiteSpace(variant.ProductTitle)
            ? variant.VariantTitle
            : $"{variant.ProductTitle} - {variant.VariantTitle}";
    }

    private static bool IsBackorderEnabled(HaravanVariant variant)
    {
        var policy = FormatInventoryPolicy(variant.InventoryPolicy).Trim().ToLowerInvariant();
        return policy is "continue" or "true" or "1" or "allow" or "allowed" or "continue_selling";
    }

    private static string FormatInventoryPolicy(JsonElement? value)
    {
        if (value is null) return "";
        var element = value.Value;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }
}
