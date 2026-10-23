using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using UniStay.Core;

namespace UniStay.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Password,
    [Required, MaxLength(120)] string Name,
    [Required, RegularExpression("^(student|landlord)$")] string Role);
public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password);
public sealed record EmailRequest([Required, EmailAddress, MaxLength(254)] string Email);
public sealed record VerifyRequest([Required, MaxLength(2048)] string Token);
public sealed record ResetRequest(
    [Required, MaxLength(128)] string Uid,
    [Required, MaxLength(256)] string Token,
    [Required, StringLength(128, MinimumLength = 10)] string Password);
// Setters distinguish an omitted PATCH field from explicit JSON null.
public sealed class ProfilePatch
{
    private string? university, bio, schedule, cleanliness;
    private bool? smoking, share;
    [MaxLength(160)] public string? University { get => university; set { university = value; HasUniversity = true; } }
    [MaxLength(1000)] public string? Bio { get => bio; set { bio = value; HasBio = true; } }
    [RegularExpression("^(|early|late)$")] public string? Schedule { get => schedule; set { schedule = value; HasSchedule = true; } }
    public bool? Smoking { get => smoking; set { smoking = value; HasSmoking = true; } }
    [RegularExpression("^(|tidy|relaxed)$")] public string? Cleanliness { get => cleanliness; set { cleanliness = value; HasCleanliness = true; } }
    public bool? ShareWithGroup { get => share; set { share = value; HasShare = true; } }
    [JsonIgnore] public bool HasUniversity { get; private set; }
    [JsonIgnore] public bool HasBio { get; private set; }
    [JsonIgnore] public bool HasSchedule { get; private set; }
    [JsonIgnore] public bool HasSmoking { get; private set; }
    [JsonIgnore] public bool HasCleanliness { get; private set; }
    [JsonIgnore] public bool HasShare { get; private set; }
}
public sealed record ProfileResponse(string University, string Bio, string Schedule, bool? Smoking, string Cleanliness, bool ShareWithGroup)
{
    public static ProfileResponse From(StudentProfile p) => new(p.University, p.Bio, p.Schedule, p.Smoking, p.Cleanliness, p.ShareWithGroup);
}
public sealed record AccountResponse(long Id, string Email, string Name, string Role, bool EmailVerified, ProfileResponse Profile)
{
    public static AccountResponse From(Account a) => new(a.Id, a.Email, a.Name, a.Role, a.EmailVerified, ProfileResponse.From(a.Profile));
}
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, AccountResponse User);
public sealed record MessageResponse(string Detail);
public sealed class ApiFault(int status, object details) : Exception("Request rejected")
{
    public int Status { get; } = status;
    public object Details { get; } = details;
}
