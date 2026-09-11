namespace Spot.Business.Api.Models;

public class BusinessContact
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public ContactType Type { get; set; }
    public string Value { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Business Business { get; set; } = null!;
}
