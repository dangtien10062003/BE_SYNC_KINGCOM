using System.Text;
using System.Text.Json;
using KingCom.Domain.Options;
using Microsoft.Extensions.Configuration;

namespace KingCom.Infrastructure.Logging;

public sealed class JsonlSyncLogger(IConfiguration config)
{
    private readonly string _path = config.GetSection("Sync").Get<SyncOptions>()?.LogFile ?? "logs/stock-sync.jsonl";
    private readonly object _lock = new();

    public void Write(string eventName, object data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var payload = JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.UtcNow,
            @event = eventName,
            data
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        lock (_lock)
        {
            File.AppendAllText(_path, payload + Environment.NewLine, Encoding.UTF8);
        }
    }

    public IEnumerable<string> ReadLast(int take)
    {
        if (!File.Exists(_path)) return [];
        return File.ReadLines(_path).TakeLast(Math.Clamp(take, 1, 500)).ToList();
    }
}
