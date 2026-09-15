using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public async Task CreateAsync(User user, RefreshToken refreshToken, CancellationToken ct = default)
    {
        // Fast path: avoids a doomed INSERT (and the cost of unwinding a database exception) for
        // the overwhelmingly common case. On its own this is NOT race-safe — see the catch below,
        // which is what actually guarantees uniqueness under concurrent requests.
        var emailTaken = await db.Users.AsNoTracking().AnyAsync(u => u.Email == user.Email, ct);
        if (emailTaken)
            throw new DuplicateEmailException(user.Email);

        // Set through the navigation, not RefreshToken.UserId directly: user.Id is database-
        // generated (gen_random_uuid()), so it does not exist yet — EF Core's change tracker
        // fixes up the foreign key once the id comes back, as part of this same SaveChanges call.
        user.RefreshTokens.Add(refreshToken);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
        {
            // Two concurrent registrations for the same email both passed the check above; the
            // database's unique index on users.email is the actual, race-proof source of truth.
            throw new DuplicateEmailException(user.Email);
        }
    }

    private static bool IsUniqueEmailViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_users_email" };
}
