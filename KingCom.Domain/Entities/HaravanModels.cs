using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingCom.Domain.Entities;

public sealed record ProductsResponse
{
    [JsonPropertyName("products")]
    public List<HaravanProduct>? Products { get; init; }
}

public sealed record HaravanProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("variants")]
    public List<HaravanVariant>? Variants { get; init; }
}

public sealed record HaravanVariant
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("sku")]
    public string Sku { get; init; } = "";

    [JsonPropertyName("title")]
    public string? VariantTitle { get; init; }

    [JsonPropertyName("inventory_policy")]
    public JsonElement? InventoryPolicy { get; init; }

    [JsonPropertyName("product_id")]
    public long ProductId { get; init; }

    public string? ProductTitle { get; init; }
}

public sealed record InventoryLocationsResponse
{
    [JsonPropertyName("inventory_locations")]
    public List<InventoryLocationBalance>? InventoryLocations { get; init; }
}

public sealed record InventoryLocationBalance
{
    [JsonPropertyName("variant_id")]
    public long VariantId { get; init; }

    [JsonPropertyName("loc_id")]
    public long LocationId { get; init; }

    [JsonPropertyName("qty_onhand")]
    public int QuantityOnHand { get; init; }
}
