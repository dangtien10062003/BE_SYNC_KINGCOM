using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using KingCom.Application.Contracts;
using KingCom.Domain.Options;
using KingCom.Infrastructure;
using KingCom.Infrastructure.Configuration;
using KingCom.Infrastructure.Integrations;
using KingCom.Infrastructure.Inventory;
using KingCom.Infrastructure.Logging;
using KingCom.Infrastructure.Security;
using KingCom.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

EnvLoader.LoadDotEnv(Path.Combine(AppContext.BaseDirectory, ".env"));
EnvLoader.LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddKingComInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhap token dang: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var jwtKey = BuildJwtKey(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
        ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
        .ToArray();

    if (allowedOrigins is null || allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException("Can cau hinh Cors:AllowedOrigins trong .env.");
    }

    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            message = exception?.Message ?? "Backend error"
        });
    });
});

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "KingCom Dong Bo API v1");
    options.RoutePrefix = "swagger";
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var auth = context.RequestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
    if (!auth.Enabled || IsPublicApi(context.Request.Path) || context.User.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new { message = "Vui long dang nhap." });
});

app.MapGet("/api/health", () => Results.Ok(new
{
    ok = true,
    service = "Haravan Inventory Sync API",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/auth/me", (ClaimsPrincipal user, IOptionsMonitor<AuthOptions> options) => Results.Ok(new
{
    authEnabled = options.CurrentValue.Enabled,
    authenticated = !options.CurrentValue.Enabled || user.Identity?.IsAuthenticated == true,
    username = user.Identity?.IsAuthenticated == true ? user.Identity.Name : null
}));

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, IOptionsMonitor<AuthOptions> options, AuthUserStore users) =>
{
    var auth = options.CurrentValue;
    if (!auth.Enabled) return Results.Ok(new { ok = true, authEnabled = false });

    var login = request.Username?.Trim() ?? "";
    var user = await users.FindByLoginAsync(login);
    var validPassword = user is not null && user.IsActive && PasswordHasher.Verify(request.Password ?? "", user.PasswordHash);
    await users.RecordLoginAsync(user?.UserId, login, validPassword, context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString(), validPassword ? "OK" : "Invalid credentials");
    if (user is null || !validPassword) return Results.Unauthorized();

    await users.UpdateLastLoginAsync(user.UserId);
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Email, user.Email ?? ""),
        new Claim(ClaimTypes.Role, user.Role)
    };
    var token = JwtTokenFactory.CreateToken(claims, jwt, jwtKey);
    return Results.Ok(new { ok = true, token, username = user.Username, email = user.Email, displayName = user.DisplayName, role = user.Role });
});

app.MapPost("/api/auth/logout", () =>
{
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/security/users", async (HttpContext context, IOptionsMonitor<AuthOptions> options, AuthUserStore users) =>
{
    if (!options.CurrentValue.Enabled) return Results.BadRequest(new { message = "Auth dang tat." });
    if (!IsAdmin(context)) return Results.Forbid();
    return Results.Ok(new { items = await users.ListUsersAsync() });
});

app.MapPost("/api/security/users", async (HttpContext context, CreateAuthUserRequest request, IOptionsMonitor<AuthOptions> options, AuthUserStore users) =>
{
    if (!options.CurrentValue.Enabled) return Results.BadRequest(new { message = "Auth dang tat." });
    if (!IsAdmin(context)) return Results.Forbid();
    var created = await users.CreateUserAsync(request);
    return Results.Created($"/api/security/users/{created.UserId}", created);
});

app.MapPut("/api/security/users/{userId:guid}/active", async (HttpContext context, Guid userId, UpdateAuthUserActiveRequest request, IOptionsMonitor<AuthOptions> options, AuthUserStore users) =>
{
    if (!options.CurrentValue.Enabled) return Results.BadRequest(new { message = "Auth dang tat." });
    if (!IsAdmin(context)) return Results.Forbid();
    await users.SetUserActiveAsync(userId, request.IsActive);
    return Results.Ok(new { ok = true });
});

app.MapPut("/api/security/users/{userId:guid}/password", async (HttpContext context, Guid userId, UpdateAuthUserPasswordRequest request, IOptionsMonitor<AuthOptions> options, AuthUserStore users) =>
{
    if (!options.CurrentValue.Enabled) return Results.BadRequest(new { message = "Auth dang tat." });
    if (!IsAdmin(context)) return Results.Forbid();
    await users.SetPasswordAsync(userId, request.Password);
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/config/status", (IConfiguration config) =>
{
    var haravan = config.GetSection("Haravan").Get<HaravanOptions>() ?? new HaravanOptions();
    var inventory = config.GetSection("Inventory").Get<InventoryOptions>() ?? new InventoryOptions();
    return Results.Ok(new
    {
        sqlConfigured = !string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")),
        stockQueryConfigured = !string.IsNullOrWhiteSpace(inventory.StockQuery),
        haravanConfigured = !string.IsNullOrWhiteSpace(haravan.AccessToken),
        haravan.ApiBaseUrl,
        haravan.ApiVersion,
        hasHcmLocation = !string.IsNullOrWhiteSpace(haravan.LocationHcmId),
        hasHnLocation = !string.IsNullOrWhiteSpace(haravan.LocationHnId),
        haravan.BatchSize,
        haravan.BlockWrites
    });
});

app.MapPost("/api/haravan/test", async (HaravanClient haravan) =>
{
    await haravan.TestConnectionAsync();
    return Results.Ok(new { ok = true, message = "Ket noi Haravan thanh cong." });
});

app.MapGet("/api/haravan/backorder-variants", async (InventorySyncService sync) =>
{
    var result = await sync.FindBackorderEnabledAsync();
    return Results.Ok(result);
});

app.MapPut("/api/haravan/backorder-variants", async (BackorderUpdateRequest request, InventorySyncService sync) =>
{
    var result = await sync.SetBackorderPolicyAsync(request);
    return Results.Ok(result);
});

app.MapPost("/api/inventory/analyze", async (InventorySyncService sync) =>
{
    var result = await sync.AnalyzeAsync();
    return Results.Ok(result);
});

app.MapGet("/api/inventory/source", async (InventoryReader reader) =>
{
    var rows = await reader.ReadAsync();
    var items = rows.Select((row, index) => new AnalyzeItem(
        Id: $"{row.Sku}-source",
        RowNumber: index + 1,
        Sku: row.Sku,
        NormalizedSku: row.Sku.Trim().ToUpperInvariant(),
        ProductName: row.ProductName,
        HcmStock: row.HcmStock,
        HnStock: row.HnStock,
        HaravanHcmStock: null,
        HaravanHnStock: null,
        HaravanVariant: null,
        Status: "pending",
        StatusLabel: "Cho phan tich",
        Message: "Da doc ton kho tu SQL Server",
        Errors: [],
        TotalChange: 0
    )).ToList();

    return Results.Ok(new { items, total = items.Count });
});

app.MapPost("/api/inventory/sync", async (SyncRequest request, InventorySyncService sync) =>
{
    var result = await sync.SyncAsync(request.DryRun);
    return Results.Ok(result);
});

app.MapGet("/api/sync/logs", (JsonlSyncLogger logger, int? take) =>
{
    return Results.Ok(new { logs = logger.ReadLast(take ?? 100) });
});

app.Run();

static bool IsPublicApi(PathString path)
{
    return path.StartsWithSegments("/api/health")
        || path.StartsWithSegments("/api/auth")
        || path.StartsWithSegments("/swagger");
}

static bool IsAdmin(HttpContext context)
{
    return context.User.IsInRole("admin");
}

static SymmetricSecurityKey BuildJwtKey(JwtOptions options)
{
    if (string.IsNullOrWhiteSpace(options.Secret) || options.Secret.Length < 32)
    {
        throw new InvalidOperationException("Can cau hinh Jwt:Secret toi thieu 32 ky tu trong .env.");
    }

    return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
}

public static class JwtTokenFactory
{
    public static string CreateToken(IEnumerable<Claim> claims, JwtOptions options, SecurityKey key)
    {
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(Math.Max(5, options.ExpireMinutes)),
            signingCredentials: credentials);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
