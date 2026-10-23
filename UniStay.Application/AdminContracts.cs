using System.Globalization;
using UniStay.Core;
namespace UniStay.Application;

public sealed record AdminListingResponse(long Id, long OwnerId, string OwnerName, string Title, string Description, string City,
    string District, string Address, string University, string Price, string Deposit, bool UtilitiesIncluded, int Capacity,
    DateOnly? MoveInDate, string Status, int Occupied, int Available, bool GroupReady, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static AdminListingResponse From(Listing l)
    {
        var occupied = l.Applications.Count(a => a.Status == "accepted");
        return new(l.Id, l.OwnerId, l.Owner.Name, l.Title, l.Description, l.City, l.District, l.Address, l.University,
            l.Price.ToString("F2", CultureInfo.InvariantCulture), l.Deposit.ToString("F2", CultureInfo.InvariantCulture), l.UtilitiesIncluded,
            l.Capacity, l.MoveInDate, l.Status, occupied, l.Capacity - occupied, occupied == l.Capacity, l.CreatedAt, l.UpdatedAt);
    }
}
public sealed record AdminPhotoResponse(long Id, long ListingId, string Image, string Caption, DateTimeOffset CreatedAt);
public sealed record AdminApplicationResponse(long Id, long StudentId, string StudentName, long ListingId, string ListingTitle, string Status, string Message, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static AdminApplicationResponse From(HousingApplication a) => new(a.Id, a.StudentId, a.Student.Name, a.ListingId, a.Listing.Title, a.Status, a.Message, a.CreatedAt, a.UpdatedAt);
}
public sealed record AdminEventResponse(long Id, long ApplicationId, long ActorId, string FromStatus, string ToStatus, DateTimeOffset CreatedAt)
{
    public static AdminEventResponse From(ApplicationEvent e) => new(e.Id, e.HousingApplicationId, e.ActorId, e.FromStatus, e.ToStatus, e.CreatedAt);
}
