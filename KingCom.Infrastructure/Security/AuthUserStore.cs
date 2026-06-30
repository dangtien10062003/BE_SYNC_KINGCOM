using KingCom.Application.Contracts;
using KingCom.Domain.Entities;
using KingCom.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace KingCom.Infrastructure.Security;

public sealed class AuthUserStore(AuthSqlConnectionFactory factory)
{
    public async Task<List<AuthUserListItem>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT UserId, Username, Email, DisplayName, Role, IsActive, CreatedAt, LastLoginAt
            FROM dbo.AuthUsers
            ORDER BY Username
            """, connection);

        var users = new List<AuthUserListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AuthUserListItem(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return users;
    }

    public async Task<AuthUserListItem> CreateUserAsync(CreateAuthUserRequest request, CancellationToken cancellationToken = default)
    {
        var username = (request.Username ?? "").Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        var role = NormalizeRole(request.Role);
        var password = request.Password ?? "";

        if (username.Length < 3) throw new InvalidOperationException("Username phải có ít nhất 3 ký tự.");
        if (password.Length < 8) throw new InvalidOperationException("Mật khẩu phải có ít nhất 8 ký tự.");

        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO dbo.AuthUsers(Username, Email, DisplayName, PasswordHash, Role, IsActive)
            OUTPUT inserted.UserId, inserted.Username, inserted.Email, inserted.DisplayName, inserted.Role, inserted.IsActive, inserted.CreatedAt, inserted.LastLoginAt
            VALUES(@username, @email, @displayName, @passwordHash, @role, @isActive)
            """, connection);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@email", email is null ? DBNull.Value : email);
        command.Parameters.AddWithValue("@displayName", displayName is null ? DBNull.Value : displayName);
        command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("@role", role);
        command.Parameters.AddWithValue("@isActive", request.IsActive);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Không thể tạo được người dùng.");
        return new AuthUserListItem(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<AuthUser?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (1) UserId, Username, Email, DisplayName, PasswordHash, Role, IsActive
            FROM dbo.AuthUsers
            WHERE Username = @login OR Email = @login
            """, connection);
        command.Parameters.AddWithValue("@login", login);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AuthUser(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6));
    }

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.AuthUsers SET LastLoginAt = SYSDATETIMEOFFSET() WHERE UserId = @userId", connection);
        command.Parameters.AddWithValue("@userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.AuthUsers SET IsActive = @isActive WHERE UserId = @userId", connection);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@isActive", isActive);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0) throw new InvalidOperationException("Không tìm thấy người dùng auth.");
    }

    public async Task SetPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Mật khẩu phải có ít nhất 8 ký tự.");
        }

        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("UPDATE dbo.AuthUsers SET PasswordHash = @passwordHash WHERE UserId = @userId", connection);
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(password));
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0) throw new InvalidOperationException("Không tìm thấy người dùng auth.");
    }

    public async Task RecordLoginAsync(Guid? userId, string login, bool success, string? ipAddress, string? userAgent, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO dbo.AuthLoginAudit(UserId, LoginName, Success, IpAddress, UserAgent, Message)
            VALUES(@userId, @loginName, @success, @ipAddress, @userAgent, @message)
            """, connection);
        command.Parameters.AddWithValue("@userId", userId is null ? DBNull.Value : userId.Value);
        command.Parameters.AddWithValue("@loginName", login);
        command.Parameters.AddWithValue("@success", success);
        command.Parameters.AddWithValue("@ipAddress", string.IsNullOrWhiteSpace(ipAddress) ? DBNull.Value : ipAddress);
        command.Parameters.AddWithValue("@userAgent", string.IsNullOrWhiteSpace(userAgent) ? DBNull.Value : userAgent);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeRole(string? role)
    {
        var normalized = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim().ToLowerInvariant();
        return normalized is "admin" ? "admin" : "user";
    }
}
