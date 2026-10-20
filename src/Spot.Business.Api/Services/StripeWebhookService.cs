using Microsoft.Extensions.Options;
using Spot.Business.Api.Billing;
using Spot.Business.Api.Models;
using Spot.Business.Api.Options;
using Spot.Business.Api.Repositories;

namespace Spot.Business.Api.Services;

/// <remarks>
/// <para>
/// Events are only used as a trigger: each handler re-reads the subscription or invoice from the
/// Stripe API and stores its current state. That makes every handler idempotent (the same event
/// twice writes the same values), makes the order events arrive in irrelevant (an old
/// customer.subscription.updated can't overwrite newer state), and avoids depending on the event's
/// API version.
/// </para>
/// <para>
/// `stripe listen` doesn't retry failed deliveries, so no handler relies on another event having
/// arrived first: an invoice for a subscription Spot hasn't stored yet creates the row from the
/// subscription itself (its metadata.accountId was set at checkout).
/// </para>
/// </remarks>
public sealed class StripeWebhookService(
    IStripeBillingGateway stripe,
    IStripeWebhookSecretProvider secretProvider,
    IBillingRepository repository,
    IOptions<StripeOptions> stripeOptions,
    TimeProvider timeProvider,
    ILogger<StripeWebhookService> logger) : IStripeWebhookService
{
    public async Task HandleAsync(string json, string? signatureHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new InvalidStripeWebhookException("The Stripe-Signature header is missing.");

        var secret = await secretProvider.GetSecretAsync(ct);
        var stripeEvent = stripe.VerifyWebhook(json, signatureHeader, secret);

        await ProcessAsync(stripeEvent, ct);
    }

    /// <summary>Applies an already verified event. Separate from <see cref="HandleAsync"/> so it can be tested on its own.</summary>
    public async Task ProcessAsync(StripeWebhookEvent stripeEvent, CancellationToken ct = default)
    {
        if (stripeEvent.ObjectId is not { } objectId)
        {
            logger.LogInformation("Stripe event {EventId} ({EventType}) has no object id; ignored.", stripeEvent.Id, stripeEvent.Type);
            return;
        }

        switch (stripeEvent.Type)
        {
            case StripeEventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompletedAsync(objectId, ct);
                break;
            case StripeEventTypes.CustomerSubscriptionUpdated:
            case StripeEventTypes.CustomerSubscriptionDeleted:
                await SyncSubscriptionAsync(objectId, accountHint: null, ct);
                break;
            case StripeEventTypes.InvoicePaid:
                await RecordInvoiceAsync(objectId, paid: true, ct);
                break;
            case StripeEventTypes.InvoicePaymentFailed:
                await RecordInvoiceAsync(objectId, paid: false, ct);
                break;
            default:
                logger.LogDebug("Stripe event {EventId} ({EventType}) isn't handled; ignored.", stripeEvent.Id, stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutCompletedAsync(string sessionId, CancellationToken ct)
    {
        var session = await stripe.GetCheckoutSessionAsync(sessionId, ct);
        if (session.Mode != "subscription" || session.SubscriptionId is null)
        {
            logger.LogInformation("Checkout session {SessionId} isn't a subscription checkout; ignored.", sessionId);
            return;
        }

        // client_reference_id is Spot's account id too; it's only a fallback for the subscription's metadata.
        Guid? accountHint = Guid.TryParse(session.ClientReferenceId, out var accountId) ? accountId : null;
        await SyncSubscriptionAsync(session.SubscriptionId, accountHint, ct);
    }

    /// <summary>
    /// Stores the subscription's current state from Stripe: updates the row with that
    /// stripe_subscription_id, or creates it. Returns the row, or null if the subscription isn't
    /// (or can't yet be) a Spot one.
    /// </summary>
    private async Task<Subscription?> SyncSubscriptionAsync(string stripeSubscriptionId, Guid? accountHint, CancellationToken ct)
    {
        var snapshot = await stripe.GetSubscriptionAsync(stripeSubscriptionId, ct);
        var status = StripeSubscriptionMapping.MapStatus(snapshot.Status);
        var plan = await FindPlanByPriceAsync(snapshot.PriceId, ct);

        var existing = await repository.GetByStripeSubscriptionIdAsync(stripeSubscriptionId, ct);
        if (existing is not null)
            return await UpdateAsync(existing, snapshot, status, plan, ct);

        if ((snapshot.AccountId ?? accountHint) is not { } accountId)
        {
            // e.g. `stripe trigger invoice.paid`: a fixture customer, not a Spot checkout.
            logger.LogInformation("Stripe subscription {StripeSubscriptionId} has no Spot account; ignored.", stripeSubscriptionId);
            return null;
        }

        if (status is not { } mappedStatus)
        {
            logger.LogInformation(
                "Stripe subscription {StripeSubscriptionId} is {StripeStatus}; nothing to store yet.", stripeSubscriptionId, snapshot.Status);
            return null;
        }

        if (plan is null)
        {
            logger.LogWarning(
                "Stripe subscription {StripeSubscriptionId} is for price {PriceId}, which matches no plan; ignored.",
                stripeSubscriptionId, snapshot.PriceId);
            return null;
        }

        var subscription = new Subscription
        {
            AccountId = accountId,
            PlanId = plan.Id,
            Status = mappedStatus,
            StripeCustomerId = snapshot.CustomerId,
            StripeSubscriptionId = snapshot.Id,
            CurrentPeriodEnd = snapshot.CurrentPeriodEnd,
            CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd,
        };

        switch (await repository.AddSubscriptionAsync(subscription, ct))
        {
            case SubscriptionInsertResult.Added:
                subscription.Plan = plan;
                logger.LogInformation(
                    "Subscription {StripeSubscriptionId} stored for account {AccountId} as {Status}.",
                    stripeSubscriptionId, accountId, mappedStatus);
                return subscription;

            case SubscriptionInsertResult.DuplicateStripeSubscription:
                // The same subscription was stored concurrently by another event: update that row instead.
                var stored = await repository.GetByStripeSubscriptionIdAsync(stripeSubscriptionId, ct);
                return stored is null ? null : await UpdateAsync(stored, snapshot, status, plan, ct);

            default:
                // Known limitation (#138): two checkouts paid at once (e.g. two tabs). The second Stripe
                // subscription is real and charged, but Spot keeps one subscription per account, so it
                // has to be canceled (and refunded) by hand in the Stripe dashboard.
                logger.LogError(
                    "Account {AccountId} already has an active subscription; Stripe subscription {StripeSubscriptionId} was NOT stored. Cancel it in the Stripe dashboard.",
                    accountId, stripeSubscriptionId);
                return null;
        }
    }

    private async Task<Subscription> UpdateAsync(
        Subscription subscription, StripeSubscriptionSnapshot snapshot, SubscriptionStatus? status, Plan? plan, CancellationToken ct)
    {
        // An unmapped status (incomplete, or one Stripe adds later) never changes a stored row.
        if (status is { } mappedStatus && StripeSubscriptionMapping.Apply(subscription, snapshot, mappedStatus, plan))
        {
            await repository.SaveChangesAsync(ct);
            logger.LogInformation(
                "Subscription {StripeSubscriptionId} updated: {Status}, period end {CurrentPeriodEnd}, cancel at period end {CancelAtPeriodEnd}.",
                subscription.StripeSubscriptionId, subscription.Status, subscription.CurrentPeriodEnd, subscription.CancelAtPeriodEnd);
        }

        return subscription;
    }

    /// <summary>
    /// Records an invoice as a payment, once per invoice. A FAILED payment becomes PAID when a later
    /// retry of the same invoice succeeds; a PAID one never goes back to FAILED.
    /// </summary>
    private async Task RecordInvoiceAsync(string invoiceId, bool paid, CancellationToken ct)
    {
        var invoice = await stripe.GetInvoiceAsync(invoiceId, ct);
        if (invoice.SubscriptionId is null)
        {
            logger.LogInformation("Invoice {InvoiceId} isn't for a subscription; ignored.", invoiceId);
            return;
        }

        // Also brings the subscription up to date (a renewal moves current_period_end; a failure
        // makes it past_due), and creates it if checkout.session.completed hasn't been processed yet.
        var subscription = await SyncSubscriptionAsync(invoice.SubscriptionId, accountHint: null, ct);
        if (subscription is null)
            return;

        if (!string.Equals(invoice.Currency, "crc", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Invoice {InvoiceId} is in {Currency}, not CRC; not recorded.", invoiceId, invoice.Currency);
            return;
        }

        var status = paid ? SubscriptionPaymentStatus.PAID : SubscriptionPaymentStatus.FAILED;
        var amount = StripeSubscriptionMapping.CrcFromMinorUnits(paid ? invoice.AmountPaid : invoice.AmountDue);
        // A PAID payment always has a paid_at: Stripe's own when it sends one, otherwise when Spot learned of it.
        DateTimeOffset? paidAt = paid ? invoice.PaidAt ?? timeProvider.GetUtcNow() : null;

        var payment = await repository.GetPaymentByInvoiceIdAsync(invoiceId, ct);
        if (payment is null)
        {
            payment = new SubscriptionPayment
            {
                SubscriptionId = subscription.Id,
                StripeInvoiceId = invoiceId,
                Status = status,
                AmountCrc = amount,
                PaidAt = paidAt,
            };

            if (await repository.AddPaymentAsync(payment, ct))
            {
                logger.LogInformation("Invoice {InvoiceId} recorded as {Status}.", invoiceId, status);
                return;
            }

            // Recorded concurrently by the same event: fall through to the transition rule on that row.
            payment = await repository.GetPaymentByInvoiceIdAsync(invoiceId, ct);
            if (payment is null)
                return;
        }

        if (paid && payment.Status == SubscriptionPaymentStatus.FAILED)
        {
            payment.Status = SubscriptionPaymentStatus.PAID;
            payment.AmountCrc = amount;
            payment.PaidAt = paidAt;
            await repository.SaveChangesAsync(ct);
            logger.LogInformation("Invoice {InvoiceId} paid after a failed attempt.", invoiceId);
        }
    }

    private async Task<Plan?> FindPlanByPriceAsync(string? priceId, CancellationToken ct)
    {
        if (priceId is null)
            return null;

        var options = stripeOptions.Value;
        var plans = await repository.ListPlansAsync(ct);
        return plans.FirstOrDefault(p => options.PriceIdFor(p) == priceId);
    }
}
