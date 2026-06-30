namespace KingCom.Domain.Entities;

public sealed record AuthUser(Guid UserId, string Username, string? Email, string? DisplayName, string PasswordHash, string Role, bool IsActive);

public sealed record AuthUserListItem(Guid UserId, string Username, string? Email, string? DisplayName, string Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);
