using Microsoft.EntityFrameworkCore;
using Npgsql;
using Spot.Business.Api.Data;
using Spot.Business.Api.Models;

namespace Spot.Business.Api.Repositories;

public sealed class BillingRepository(BusinessDbContext db) : IBillingRepository
{
    public async Task<IReadOnlyList<Plan>> ListActivePlansAsync(CancellationToken ct = default) =>
        await db.Plans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.PriceCrc)
            .ThenBy(p => p.Code)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Plan>> ListPlansAsync(CancellationToken ct = default) =>
        await db.Plans.AsNoTracking().ToListAsync(ct);

    public Task<Plan?> GetPlanByCodeAsync(PlanCode code, CancellationToken ct = default) =>
        db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);

    public Task<Subscription?> GetCurrentSubscriptionAsync(Guid accountId, CancellationToken ct = default) =>
        db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.AccountId == accountId
                && (s.Status == SubscriptionStatus.ACTIVE || s.Status == SubscriptionStatus.PAST_DUE), ct);

    public Task<string?> GetLatestStripeCustomerIdAsync(Guid accountId, CancellationToken ct = default) =>
        db.Subscriptions.AsNoTracking()
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.StripeCustomerId)
            .FirstOrDefaultAsync(ct);

    public Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken ct = default) =>
        db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId, ct);

    public async Task<SubscriptionInsertResult> AddSubscriptionAsync(Subscription subscription, CancellationToken ct = default)
    {
        db.Subscriptions.Add(subscription);
        try
        {
            await db.SaveChangesAsync(ct);
            return SubscriptionInsertResult.Added;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_subscriptions_stripe_subscription_id"))
        {
            db.Entry(subscription).State = EntityState.Detached;
            return SubscriptionInsertResult.DuplicateStripeSubscription;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_subscriptions_one_current"))
        {
            db.Entry(subscription).State = EntityState.Detached;
            return SubscriptionInsertResult.AccountAlreadySubscribed;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task<SubscriptionPayment?> GetPaymentByInvoiceIdAsync(string stripeInvoiceId, CancellationToken ct = default) =>
        db.SubscriptionPayments.FirstOrDefaultAsync(p => p.StripeInvoiceId == stripeInvoiceId, ct);

    public async Task<bool> AddPaymentAsync(SubscriptionPayment payment, CancellationToken ct = default)
    {
        db.SubscriptionPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_subscription_payments_stripe_invoice_id"))
        {
            db.Entry(payment).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<(IReadOnlyList<SubscriptionPayment> Items, int Total)> ListPaymentsAsync(
        Guid accountId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.SubscriptionPayments.AsNoTracking().Where(p => p.Subscription.AccountId == accountId);

        var total = await query.CountAsync(ct);

        // Offset as long so a huge page can't overflow int into a negative OFFSET (same guard as
        // ServiceOfferingRepository.ListAsync). Past the last row there's nothing to return.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total)
            return ([], total);

        var items = await query
            .OrderBy(p => p.PaidAt == null)
            .ThenByDescending(p => p.PaidAt)
            .ThenBy(p => p.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;
}
