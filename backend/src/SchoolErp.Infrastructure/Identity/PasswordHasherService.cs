using Microsoft.AspNetCore.Identity;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Infrastructure.Identity;

/// <summary>Adapter over ASP.NET Core Identity's PBKDF2 hasher so the algorithm is replaceable.</summary>
public sealed class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<UserAccount> _inner = new();

    public string Hash(string password) => _inner.HashPassword(new UserAccount(), password);

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrWhiteSpace(hash)) return false;
        var result = _inner.VerifyHashedPassword(new UserAccount(), hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
