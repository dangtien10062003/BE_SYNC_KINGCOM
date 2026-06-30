using System.Text.RegularExpressions;
using KingCom.Domain.Entities;
using KingCom.Domain.Options;
using KingCom.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace KingCom.Infrastructure.Inventory;

public sealed class InventoryReader(SqlConnectionFactory factory, IConfiguration config)
{
    private static readonly Regex DangerousSql = new(@"\b(update|delete|insert|drop|alter|truncate|merge|exec|execute|create|grant|revoke)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly string _query = config.GetSection("Inventory").Get<InventoryOptions>()?.StockQuery ?? "";

    public async Task<List<StockRow>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var query = ValidateSelectQuery(_query);
        var rows = new List<StockRow>();

        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(query, connection)
        {
            CommandType = System.Data.CommandType.Text,
            CommandTimeout = 60
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var skuIndex = reader.GetOrdinal("sku");
        var hcmIndex = reader.GetOrdinal("hcmStock");
        var hnIndex = reader.GetOrdinal("hnStock");
        var productNameIndex = TryGetOrdinal(reader, "productName");

        while (await reader.ReadAsync(cancellationToken))
        {
            var sku = Convert.ToString(reader.GetValue(skuIndex))?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(sku)) continue;

            rows.Add(new StockRow(
                Sku: sku,
                HcmStock: ToStock(reader.GetValue(hcmIndex)),
                HnStock: ToStock(reader.GetValue(hnIndex)),
                ProductName: productNameIndex >= 0 ? Convert.ToString(reader.GetValue(productNameIndex))?.Trim() ?? "" : ""));
        }

        return rows;
    }

    private static string ValidateSelectQuery(string query)
    {
        var cleaned = StripComments(query).Trim().TrimEnd(';').Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) throw new InvalidOperationException("Inventory:StockQuery dang rong.");
        if (cleaned.Contains(';')) throw new InvalidOperationException("Chi cho phep mot cau SELECT.");

        var firstWord = cleaned.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        if (firstWord is not ("select" or "with")) throw new InvalidOperationException("Inventory:StockQuery phai bat dau bang SELECT hoac WITH.");
        if (DangerousSql.IsMatch(cleaned)) throw new InvalidOperationException("Inventory:StockQuery chua keyword nguy hiem.");
        return cleaned;
    }

    private static string StripComments(string query)
    {
        var withoutBlock = Regex.Replace(query, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"--.*?$", "", RegexOptions.Multiline);
    }

    private static int ToStock(object value)
    {
        if (value is DBNull or null) return 0;
        var stock = Convert.ToInt32(Math.Truncate(Convert.ToDecimal(value)));
        return Math.Max(0, stock);
    }

    private static int TryGetOrdinal(SqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
