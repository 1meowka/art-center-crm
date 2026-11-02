#nullable enable

namespace VKR.Models;

public sealed class AuthResult
{
    public AppUser? User { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsSuccess => User != null;

    public static AuthResult Success(AppUser user) => new() { User = user };

    public static AuthResult Fail(string message) => new() { ErrorMessage = message };
}
