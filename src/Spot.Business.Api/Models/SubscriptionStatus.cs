namespace Spot.Business.Api.Models;

/// <summary>
/// The native "subscription_status" enum (#138). Labels are kept UPPERCASE, same as db/Spot.sql
/// and the contract (see Program.cs).
/// </summary>
public enum SubscriptionStatus
{
    ACTIVE,
    PAST_DUE,
    CANCELED,
    EXPIRED
}
