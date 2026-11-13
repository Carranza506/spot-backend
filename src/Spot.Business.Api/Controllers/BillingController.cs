using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Spot.Business.Api.Billing;
using Spot.Business.Api.DTOs;
using Spot.Business.Api.Services;
using Spot.Shared.Errors;
using Spot.Shared.Pagination;

namespace Spot.Business.Api.Controllers;

/// <summary>
/// contracts/spot-api.yaml, "Business" tag, "BUSINESS - Billing" endpoints (#138): business plans
/// paid through Stripe Checkout, test mode only. The plan list and the Stripe webhook are public,
/// so [Authorize] is applied per-action, same as ServicesController.
/// </summary>
[ApiController]
[Route("business/billing")]
public class BillingController(IBillingService billingService, IStripeWebhookService webhookService) : ControllerBase
{
    /// <summary>GET /business/billing/plans: public. Active plans with their (informational) features.</summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPlans(CancellationToken ct) =>
        Ok(await billingService.ListPlansAsync(ct));

    /// <summary>GET /business/billing/subscription: the caller's effective plan (FREE if none).</summary>
    [HttpGet("subscription")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMySubscription(CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        return Ok(await billingService.GetMySubscriptionAsync(callerId, ct));
    }

    /// <summary>
    /// POST /business/billing/checkout: returns the Stripe Checkout URL. Activates nothing — only
    /// the verified webhook does, after Stripe confirms the payment.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(CheckoutSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutSessionRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        try
        {
            return Ok(await billingService.CreateCheckoutSessionAsync(callerId, request.PlanCode, ct));
        }
        catch (InvalidPlanException)
        {
            return BadRequest(new ApiError(
                "BAD_REQUEST", "El plan indicado no existe o no se puede contratar.", new { field = "planCode" }));
        }
        catch (AlreadySubscribedException)
        {
            return Conflict(new ApiError("ALREADY_SUBSCRIBED", "Ya tienes una suscripción activa a este plan."));
        }
    }

    /// <summary>POST /business/billing/subscription/cancel: cancels at the end of the period. Idempotent.</summary>
    [HttpPost("subscription/cancel")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelMySubscription(CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        var result = await billingService.CancelMySubscriptionAsync(callerId, ct);
        return result is null
            ? NotFound(new ApiError("NOT_FOUND", "No tienes una suscripción activa."))
            : Ok(result);
    }

    /// <summary>GET /business/billing/payments: the caller's payment history, paginated.</summary>
    [HttpGet("payments")]
    [Authorize(Roles = "BUSINESS")]
    [ProducesResponseType(typeof(PaginatedResponse<SubscriptionPaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListMyPayments([FromQuery] PaymentListQuery query, CancellationToken ct)
    {
        if (!TryGetUserId(out var callerId))
            return InvalidToken();

        if (!query.IsValid)
            return BadRequest(new ApiError("BAD_REQUEST", "page debe ser >= 1 y pageSize entre 1 y 100."));

        return Ok(await billingService.ListMyPaymentsAsync(callerId, query.Page, query.PageSize, ct));
    }

    /// <summary>
    /// POST /business/billing/webhooks/stripe: called by Stripe (in development, `stripe listen`).
    /// No JWT: the Stripe-Signature header is verified against the raw body instead, so the body is
    /// read as is and never model-bound. Unhandled event types are acknowledged with 200.
    /// </summary>
    [HttpPost("webhooks/stripe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        string json;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            json = await reader.ReadToEndAsync(ct);

        try
        {
            await webhookService.HandleAsync(json, Request.Headers["Stripe-Signature"].FirstOrDefault(), ct);
            return Ok();
        }
        catch (InvalidStripeWebhookException)
        {
            return BadRequest(new ApiError("BAD_REQUEST", "La firma del webhook de Stripe no es válida."));
        }
    }

    private UnauthorizedObjectResult InvalidToken() =>
        Unauthorized(new ApiError("UNAUTHORIZED", "Token de acceso inválido o ausente."));

    /// <summary>Same as BusinessesController.TryGetUserId: User.Identity.Name IS the caller's account id (JWT sub).</summary>
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.Identity?.Name, out userId);
}
