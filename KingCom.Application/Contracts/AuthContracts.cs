namespace KingCom.Application.Contracts;

public sealed record LoginRequest(string? Username, string? Password);
public sealed record CreateAuthUserRequest(string? Username, string? Email, string? DisplayName, string? Password, string? Role = "user", bool IsActive = true);
public sealed record UpdateAuthUserActiveRequest(bool IsActive);
public sealed record UpdateAuthUserPasswordRequest(string? Password);
