using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KingCom.Domain.Entities;
using KingCom.Domain.Options;
using Microsoft.Extensions.Configuration;

namespace KingCom.Infrastructure.Integrations;

public sealed class HaravanClient(HttpClient httpClient, IConfiguration config)
{
    private readonly HaravanOptions _options = config.GetSection("Haravan").Get<HaravanOptions>() ?? new HaravanOptions();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "shop.json", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<HaravanVariant>> FetchVariantsAsync(CancellationToken cancellationToken = default)
    {
        var variants = new List<HaravanVariant>();
        var page = 1;

        while (true)
        {
            using var response = await SendWithRetryAsync(HttpMethod.Get, $"products.json?limit=50&page={page}&fields=id,title,variants", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<ProductsResponse>(_json, cancellationToken) ?? new ProductsResponse();
            var products = data.Products ?? [];
            if (products.Count == 0) break;

            foreach (var product in products)
            {
                foreach (var variant in product.Variants ?? [])
                {
                    variants.Add(variant with
                    {
                        ProductId = product.Id != 0 ? product.Id : variant.ProductId,
                        ProductTitle = product.Title
                    });
                }
            }

            page += 1;
            if (page > 500) throw new InvalidOperationException("Haravan products pagination vuot qua gioi han an toan.");
        }

        return variants;
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> FetchBalancesAsync(IEnumerable<long> variantIds, IEnumerable<string> locationIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, Dictionary<string, int>>();
        var locations = string.Join(",", locationIds.Where(id => !string.IsNullOrWhiteSpace(id)));
        var uniqueIds = variantIds.Where(id => id > 0).Distinct().Select(id => id.ToString()).ToList();

        foreach (var chunk in uniqueIds.Chunk(50))
        {
            var ids = string.Join(",", chunk);
            using var response = await SendWithRetryAsync(HttpMethod.Get, $"inventory_locations.json?limit=250&location_ids={locations}&variant_ids={ids}", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<InventoryLocationsResponse>(_json, cancellationToken) ?? new InventoryLocationsResponse();

            foreach (var item in data.InventoryLocations ?? [])
            {
                var variantId = item.VariantId.ToString();
                var locationId = item.LocationId.ToString();
                if (!result.TryGetValue(variantId, out var byLocation))
                {
                    byLocation = new Dictionary<string, int>();
                    result[variantId] = byLocation;
                }
                byLocation[locationId] = item.QuantityOnHand;
            }
        }

        return result;
    }

    public async Task SetInventoryAsync(string locationId, IReadOnlyList<InventoryUpdateLine> lines, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            inventory = new
            {
                location_id = long.Parse(locationId),
                type = "set",
                reason = _options.Reason,
                note = _options.Note,
                line_items = lines.Select(line => new
                {
                    product_id = line.ProductId,
                    product_variant_id = line.ProductVariantId,
                    quantity = line.Quantity
                }).ToList()
            }
        };

        using var response = await SendWithRetryAsync(HttpMethod.Post, "inventories/adjustorset.json", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetVariantInventoryPolicyAsync(long variantId, bool enabled, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            variant = new
            {
                id = variantId,
                inventory_policy = enabled ? "continue" : "deny"
            }
        };

        using var response = await SendWithRetryAsync(HttpMethod.Put, $"variants/{variantId}.json", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var response = await SendAsync(method, path, body, cancellationToken);
            if ((int)response.StatusCode < 400) return response;

            var retryable = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!retryable || attempt == 6) return response;

            var delay = GetRetryDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
        throw new InvalidOperationException("Retry Haravan không thành công.");
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken)) throw new InvalidOperationException("Chưa cấu hình Haravan:AccessToken.");

        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl)) throw new InvalidOperationException("Chua cau hinh Haravan:ApiBaseUrl.");
        if (string.IsNullOrWhiteSpace(_options.ApiVersion)) throw new InvalidOperationException("Chua cau hinh Haravan:ApiVersion.");

        var apiBase = _options.ApiBaseUrl.TrimEnd('/');
        var apiVersion = _options.ApiVersion.Trim('/');
        var request = new HttpRequestMessage(method, $"{apiBase}/{apiVersion}/{path.TrimStart('/')}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        }

        return await httpClient.SendAsync(request, cancellationToken);
    }
}
