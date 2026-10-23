namespace UniStay.Core;

public sealed class Listing
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public Account Owner { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string City { get; set; } = "Астана";
    public string District { get; set; } = "";
    public string Address { get; set; } = "";
    public string University { get; set; } = "";
    public decimal Price { get; set; }
    public decimal Deposit { get; set; }
    public bool UtilitiesIncluded { get; set; }
    public int Capacity { get; set; }
    public DateOnly? MoveInDate { get; set; }
    public string Status { get; set; } = "draft";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ListingPhoto> Photos { get; set; } = [];
    public List<HousingApplication> Applications { get; set; } = [];
}
public sealed class ListingPhoto
{
    public long Id { get; set; }
    public long ListingId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FileName { get; set; } = "";
    public string Caption { get; set; } = "";
}
public sealed class HousingApplication
{
    public long Id { get; set; }
    public long StudentId { get; set; }
    public Account Student { get; set; } = null!;
    public long ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ApplicationEvent> Events { get; set; } = [];
}
public sealed class ApplicationEvent
{
    public long Id { get; set; }
    public long HousingApplicationId { get; set; }
    public long ActorId { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
