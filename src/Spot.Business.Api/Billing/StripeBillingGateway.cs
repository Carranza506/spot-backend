using Microsoft.Extensions.Options;
using Spot.Business.Api.Options;
using Stripe;
using Stripe.Checkout;

namespace Spot.Business.Api.Billing;

/// <summary>
/// <see cref="IStripeBillingGateway"/> on the official Stripe.net client. Card data never passes
/// through here: Checkout hosts the payment page, and Spot only ever sees ids, statuses and amounts.
/// </summary>
/// <remarks>
/// The <see cref="StripeClient"/> is created on first use, not in the constructor, so the app (and
/// every endpoint that doesn't need Stripe) still starts when the Stripe settings are missing; the
/// first call that does need them fails with the validator's message instead.
/// </remarks>
public sealed class StripeBillingGateway(IOptions<StripeOptions> options) : IStripeBillingGateway
{
    /// <summary>Metadata key holding the Spot account id, on both the Checkout session and the subscription.</summary>
    public const string AccountIdMetadataKey = "accountId";

    /// <summary>Metadata key holding the plan code bought, for reference in the Stripe dashboard.</summary>
    public const string PlanCodeMetadataKey = "planCode";

    private StripeClient? _client;

    private StripeClient Client => _client ??= new StripeClient(options.Value.SecretKey);

    public async Task<string> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string>
        {
            [AccountIdMetadataKey] = request.AccountId.ToString(),
            [PlanCodeMetadataKey] = request.PlanCode.ToString(),
        };

        var session = await new SessionService(Client).CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = request.PriceId, Quantity = 1 }],
            Customer = request.CustomerId,
            ClientReferenceId = request.AccountId.ToString(),
            Metadata = metadata,
            // Copied onto the subscription itself, so every later subscription/invoice event can be
            // tied back to the account even if checkout.session.completed arrives late or never.
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata },
            SuccessUrl = options.Value.SuccessUrl,
            CancelUrl = options.Value.CancelUrl,
        }, cancellationToken: ct);

        return session.Url;
    }

    public async Task<StripeCheckoutSessionSnapshot> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await new SessionService(Client).GetAsync(sessionId, cancellationToken: ct);
        return new StripeCheckoutSessionSnapshot(session.Id, session.Mode, session.SubscriptionId, session.ClientReferenceId);
    }

    public async Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default) =>
        ToSnapshot(await new SubscriptionService(Client).GetAsync(subscriptionId, cancellationToken: ct));

    public async Task<StripeSubscriptionSnapshot> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default) =>
        ToSnapshot(await new SubscriptionService(Client).UpdateAsync(
            subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: ct));

    public async Task<StripeInvoiceSnapshot> GetInvoiceAsync(string invoiceId, CancellationToken ct = default)
    {
        var invoice = await new InvoiceService(Client).GetAsync(invoiceId, cancellationToken: ct);
        return new StripeInvoiceSnapshot(
            invoice.Id,
            // Since API version 2025-03-31 the subscription lives under parent.subscription_details.
            invoice.Parent?.SubscriptionDetails?.SubscriptionId,
            invoice.Currency,
            invoice.AmountPaid,
            invoice.AmountDue,
            ToOffset(invoice.StatusTransitions?.PaidAt));
    }

    public StripeWebhookEvent VerifyWebhook(string json, string signatureHeader, string secret)
    {
        Event stripeEvent;
        try
        {
            // throwOnApiVersionMismatch: false — `stripe listen` and the account may send events in
            // another API version than the one Stripe.net pins. Only the event type and the object
            // id are read here; everything else is fetched again through the API.
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new InvalidStripeWebhookException("The Stripe-Signature header couldn't be verified.", ex);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new InvalidStripeWebhookException("The webhook body isn't a Stripe event.", ex);
        }

        return new StripeWebhookEvent(stripeEvent.Id, stripeEvent.Type, (stripeEvent.Data?.Object as IHasId)?.Id);
    }

    private static StripeSubscriptionSnapshot ToSnapshot(Stripe.Subscription subscription)
    {
        // Spot's checkout always creates single-item subscriptions. Since API version 2025-03-31
        // the current period is per item, not on the subscription.
        var item = subscription.Items?.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException($"Stripe subscription {subscription.Id} has no items.");

        Guid? accountId = subscription.Metadata is not null
            && subscription.Metadata.TryGetValue(AccountIdMetadataKey, out var raw)
            && Guid.TryParse(raw, out var parsed)
                ? parsed
                : null;

        return new StripeSubscriptionSnapshot(
            subscription.Id,
            subscription.CustomerId,
            subscription.Status,
            ToOffset(item.CurrentPeriodEnd),
            subscription.CancelAtPeriodEnd || subscription.CancelAt is not null,
            item.Price?.Id,
            accountId);
    }

    // Stripe.net returns UTC DateTimes (Kind may be Unspecified); pin them to UTC explicitly.
    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) => value is { } v ? ToOffset(v) : null;
}
