using KingCom.Domain.Options;
using KingCom.Infrastructure.Data;
using KingCom.Infrastructure.Integrations;
using KingCom.Infrastructure.Inventory;
using KingCom.Infrastructure.Logging;
using KingCom.Infrastructure.Security;
using KingCom.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KingCom.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKingComInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultConnection = configuration.GetConnectionString("DefaultConnection") ?? "";
        var authConnection = configuration.GetConnectionString("AuthConnection") ?? "";

        services.Configure<InventoryOptions>(configuration.GetSection("Inventory"));
        services.Configure<HaravanOptions>(configuration.GetSection("Haravan"));
        services.Configure<SyncOptions>(configuration.GetSection("Sync"));
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));

        services.AddSingleton(new SqlConnectionFactory(defaultConnection));
        services.AddSingleton(new AuthSqlConnectionFactory(authConnection));
        services.AddSingleton<AuthUserStore>();
        services.AddSingleton<JsonlSyncLogger>();
        services.AddSingleton<InventorySyncGate>();
        services.AddScoped<InventoryReader>();
        services.AddHttpClient<HaravanClient>();
        services.AddScoped<InventorySyncService>();
        services.AddHostedService<AutomaticInventorySyncWorker>();

        return services;
    }
}
