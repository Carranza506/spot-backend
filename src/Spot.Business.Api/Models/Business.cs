namespace Spot.Business.Api.Models;

public class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? LegalName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public BusinessLocation? Location { get; set; }
    public ICollection<BusinessContact> Contacts { get; set; } = [];
    public ICollection<BusinessPhoto> Photos { get; set; } = [];
    public ICollection<BusinessHours> Hours { get; set; } = [];
    public ICollection<BusinessScheduleException> ScheduleExceptions { get; set; } = [];
    public ICollection<Service> Services { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
}
