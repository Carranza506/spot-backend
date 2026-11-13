using Microsoft.EntityFrameworkCore;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;
using ServiceEntity = Spot.Business.Api.Models.Service;

namespace Spot.Business.Api.Repositories;

public sealed class PhotoRepository(BusinessDbContext db) : IPhotoRepository
{
    // ---------- Business photos ----------

    public async Task<(IReadOnlyList<BusinessPhoto> Items, int Total)> ListBusinessPhotosAsync(
        Guid businessId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.BusinessPhotos.AsNoTracking().Where(p => p.BusinessId == businessId);

        var total = await query.CountAsync(ct);
        if (!TryGetOffset(page, pageSize, total, out var offset))
            return ([], total);

        var items = await query
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<BusinessPhoto?> GetBusinessPhotoAsync(Guid businessId, Guid photoId, CancellationToken ct = default) =>
        db.BusinessPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.BusinessId == businessId, ct);

    public Task AddBusinessPhotoAsync(BusinessPhoto photo, int maxPhotos, CancellationToken ct = default) =>
        InParentLockAsync(
            // Interpolated, so EF sends the id as a parameter, never as SQL text.
            lockParent: () => db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM businesses WHERE id = {photo.BusinessId} FOR UPDATE", ct),
            write: async () =>
            {
                var count = await db.BusinessPhotos.CountAsync(p => p.BusinessId == photo.BusinessId, ct);
                if (count >= maxPhotos)
                    throw new PhotoLimitReachedException(maxPhotos);

                if (photo.IsPrimary)
                {
                    var currentPrimaries = await db.BusinessPhotos
                        .Where(p => p.BusinessId == photo.BusinessId && p.IsPrimary)
                        .ToListAsync(ct);
                    foreach (var current in currentPrimaries)
                        current.IsPrimary = false;

                    // Saved on its own, before the insert: in one SaveChanges, EF could send the
                    // INSERT first and hit ux_business_photos_one_primary.
                    await db.SaveChangesAsync(ct);
                }

                db.BusinessPhotos.Add(photo);
                await db.SaveChangesAsync(ct);
            },
            ct);

    public async Task DeleteBusinessPhotoAsync(BusinessPhoto photo, CancellationToken ct = default)
    {
        db.BusinessPhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Service photos ----------

    public Task<ServiceEntity?> FindServiceAsync(Guid serviceId, CancellationToken ct = default) =>
        db.Services.AsNoTracking().Include(s => s.Business).FirstOrDefaultAsync(s => s.Id == serviceId, ct);

    public async Task<(IReadOnlyList<ServicePhoto> Items, int Total)> ListServicePhotosAsync(
        Guid serviceId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.ServicePhotos.AsNoTracking().Where(p => p.ServiceId == serviceId);

        var total = await query.CountAsync(ct);
        if (!TryGetOffset(page, pageSize, total, out var offset))
            return ([], total);

        var items = await query
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<ServicePhoto?> GetServicePhotoAsync(Guid serviceId, Guid photoId, CancellationToken ct = default) =>
        db.ServicePhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.ServiceId == serviceId, ct);

    public Task AddServicePhotoAsync(ServicePhoto photo, int maxPhotos, CancellationToken ct = default) =>
        InParentLockAsync(
            lockParent: () => db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM services WHERE id = {photo.ServiceId} FOR UPDATE", ct),
            write: async () =>
            {
                var count = await db.ServicePhotos.CountAsync(p => p.ServiceId == photo.ServiceId, ct);
                if (count >= maxPhotos)
                    throw new PhotoLimitReachedException(maxPhotos);

                db.ServicePhotos.Add(photo);
                await db.SaveChangesAsync(ct);
            },
            ct);

    public async Task DeleteServicePhotoAsync(ServicePhoto photo, CancellationToken ct = default)
    {
        db.ServicePhotos.Remove(photo);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Runs <paramref name="write"/> in a transaction that holds a row lock on the parent until
    /// commit (see <see cref="IPhotoRepository"/>). Any exception rolls the whole write back.
    /// </summary>
    /// <remarks>
    /// The InMemory provider used by the unit tests has neither transactions nor row locks, so
    /// there the write simply runs as is; the locking itself is verified against real Postgres.
    /// </remarks>
    private async Task InParentLockAsync(Func<Task<int>> lockParent, Func<Task> write, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await write();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await lockParent();
        await write();
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// The row offset of <paramref name="page"/>, computed in 64 bits so a huge page number can't
    /// overflow into a negative OFFSET (a 500 on Postgres). False when the page is past the last row.
    /// </summary>
    private static bool TryGetOffset(int page, int pageSize, int total, out int offset)
    {
        var offset64 = (long)(page - 1) * pageSize;
        offset = offset64 < total ? (int)offset64 : 0;
        return offset64 < total;
    }
}
