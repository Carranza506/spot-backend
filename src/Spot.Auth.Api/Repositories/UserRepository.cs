using Microsoft.EntityFrameworkCore;
using Spot.Auth.Api.Data;
using Spot.Auth.Api.Models;

namespace Spot.Auth.Api.Repositories;

public sealed class UserRepository(AuthDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.Include(u => u.AuthProviders).FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task SaveChangesAsync(User user, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await db.Entry(user).ReloadAsync(ct);
    }
}
