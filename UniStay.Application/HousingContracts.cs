using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniStay.Core;
namespace UniStay.Application;

public readonly record struct Optional<T>(bool IsSet, T? Value);
public sealed class OptionalConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);
    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalConverter<>).MakeGenericType(type.GetGenericArguments()[0]))!;
    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => new(true, JsonSerializer.Deserialize<T>(ref reader, options));
        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Value, options);
    }
}
public sealed class ListingRequest
{
    public Optional<string?> Title { get; set; }
    public Optional<string?> Description { get; set; }
    public Optional<string?> City { get; set; }
    public Optional<string?> District { get; set; }
    public Optional<string?> Address { get; set; }
    public Optional<string?> University { get; set; }
    public Optional<decimal?> Price { get; set; }
    public Optional<decimal?> Deposit { get; set; }
    public Optional<bool?> UtilitiesIncluded { get; set; }
    public Optional<int?> Capacity { get; set; }
    public Optional<DateOnly?> MoveInDate { get; set; }
    public Optional<string?> Status { get; set; }
}
public sealed record PhotoResponse(long Id, string Image, string Caption);
public sealed record ListingResponse(long Id, long Owner, string OwnerName, string Title, string Description,
    string City, string District, string? Address, string University, string Price, string Deposit,
    bool UtilitiesIncluded, int Capacity, DateOnly? MoveInDate, string Status, IReadOnlyList<PhotoResponse> Photos,
    int Occupied, int Available, bool GroupReady, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ListingResponse From(Listing l, long? viewer, string origin)
    {
        var occupied = l.Applications.Count(a => a.Status == "accepted");
        return new(l.Id, l.OwnerId, l.Owner.Name, l.Title, l.Description, l.City, l.District,
            viewer == l.OwnerId || l.Applications.Any(a => a.StudentId == viewer && a.Status == "accepted") ? l.Address : null,
            l.University, l.Price.ToString("F2", CultureInfo.InvariantCulture), l.Deposit.ToString("F2", CultureInfo.InvariantCulture),
            l.UtilitiesIncluded, l.Capacity, l.MoveInDate, l.Status,
            l.Photos.OrderBy(p => p.Id).Select(p => new PhotoResponse(p.Id, origin + "/media/" + p.FileName, p.Caption)).ToArray(),
            occupied, l.Capacity - occupied, l.Capacity == occupied, l.CreatedAt, l.UpdatedAt);
    }
}
public sealed record ApplyRequest([Range(1, long.MaxValue)] long ListingId, [MaxLength(1000)] string Message = "");
public sealed record EventResponse(string FromStatus, string ToStatus, DateTimeOffset CreatedAt, long ActorId);
public sealed record ApplicantResponse(long Id, string Name, ProfileResponse Profile);
public sealed record ApplicationResponse(long Id, long ListingId, string ListingTitle, ApplicantResponse Student,
    string Status, string Message, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<EventResponse> Events)
{
    public static ApplicationResponse From(HousingApplication a) => new(a.Id, a.ListingId, a.Listing.Title,
        new(a.StudentId, a.Student.Name, ProfileResponse.From(a.Student.Profile)), a.Status, a.Message, a.CreatedAt, a.UpdatedAt,
        a.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Select(e => new EventResponse(e.FromStatus, e.ToStatus, e.CreatedAt, e.ActorId)).ToArray());
}
public sealed record PageResponse<T>(int Count, string? Next, string? Previous, IReadOnlyList<T> Results);
public sealed record GroupResponse(long Id, string Name, string University, string Bio, string Schedule, bool? Smoking,
    string Cleanliness, int? Compatibility, IReadOnlyList<string> MatchingPreferences, int ComparedPreferences);
public static class Access
{
    public static void Verified(Account a) { if (!a.EmailVerified) throw new ApiFault(403, "Сначала подтвердите email."); }
    public static void Role(Account a, string role) { if (a.Role != role) throw new ApiFault(403, "Эта операция недоступна вашей роли."); Verified(a); }
}
