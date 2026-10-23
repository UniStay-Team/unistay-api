using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniStay.Application;
using UniStay.Core;
namespace UniStay.Api;

[Authorize, Route("api/v1/admin")]
public sealed class AdminController(UniStayDb db, AdminService admin, HousingService housing) : HousingControllerBase
{
    private void Query(params string[] allowed)
    {
        foreach (var key in Request.Query.Keys)
            if (key != "page" && !allowed.Contains(key) || Request.Query[key].Count != 1) throw new ApiFault(400, "Неизвестный или повторный фильтр.");
        if (Request.Query.ContainsKey("page") && (!int.TryParse(Value("page"), out var page) || page < 1)) throw new ApiFault(400, "Некорректная страница.");
    }
    private string Value(string key) => Request.Query[key].ToString();
    private long? IdFilter(string key)
    {
        if (!Request.Query.ContainsKey(key)) return null;
        if (!long.TryParse(Value(key), out var value) || value < 1) throw new ApiFault(400, "Некорректный ID.");
        return value;
    }
    private bool? BoolFilter(string key)
    {
        if (!Request.Query.ContainsKey(key)) return null;
        return Value(key) switch { "true" => true, "false" => false, _ => throw new ApiFault(400, "Ожидается true/false.") };
    }
    private string? Choice(string key, params string[] choices)
    {
        if (!Request.Query.ContainsKey(key)) return null;
        var value = Value(key); if (!choices.Contains(value)) throw new ApiFault(400, "Неизвестное значение фильтра."); return value;
    }
    [HttpGet("me/")]
    public AdminMeResponse Me()
    {
        if (!Actor.IsStaff || !Actor.EmailVerified) throw new ApiFault(403, "Требуются административные права.");
        return new(Actor.Id, Actor.Email, Actor.Name, Actor.IsSuperuser ? AdminService.KnownPermissions : Actor.Permissions.Where(AdminService.KnownPermissions.Contains).ToArray());
    }
    [HttpGet("users/")]
    public Task<PageResponse<AdminUserResponse>> Users(CancellationToken ct)
    {
        AdminService.Require(Actor, "users.read"); Query("search", "role", "is_active", "email_verified");
        var q = db.Accounts.AsNoTracking(); var search = Value("search").ToLowerInvariant();
        if (search != "") q = q.Where(a => a.Email.ToLower().Contains(search) || a.Name.ToLower().Contains(search));
        var role = Choice("role", "student", "landlord"); if (role is not null) q = q.Where(a => a.Role == role);
        var active = BoolFilter("is_active"); if (active is not null) q = q.Where(a => a.IsActive == active);
        var verified = BoolFilter("email_verified"); if (verified is not null) q = q.Where(a => a.EmailVerified == verified);
        return PageAsync(q.OrderBy(a => a.Id), AdminUserResponse.From, ct);
    }
    [HttpGet("users/{id:long}/")]
    public async Task<AdminUserResponse> UserDetail(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "users.read");
        return AdminUserResponse.From(await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
    [HttpPatch("users/{id:long}/")]
    public Task<AdminUserResponse> Update(long id, AdminUserPatch input, CancellationToken ct) => admin.UpdateAsync(Actor, id, input, HttpContext.TraceIdentifier, ct);
    [HttpGet("users/{id:long}/profile/")]
    public async Task<AdminProfileResponse> Profile(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "profiles.read");
        return AdminProfileResponse.From(await db.Set<StudentProfile>().AsNoTracking().SingleOrDefaultAsync(p => p.AccountId == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
    [HttpPatch("users/{id:long}/profile/")]
    public Task<AdminProfileResponse> UpdateProfile(long id, AdminProfilePatch input, CancellationToken ct) => admin.UpdateProfileAsync(Actor, id, input, HttpContext.TraceIdentifier, ct);
    [HttpGet("listings/")]
    public Task<PageResponse<AdminListingResponse>> Listings(CancellationToken ct)
    {
        AdminService.Require(Actor, "listings.read"); Query("search", "city", "owner_id", "status");
        var q = housing.Listings.AsNoTracking(); var search = Value("search").ToLowerInvariant(); var city = Value("city").ToLowerInvariant();
        if (search != "") q = q.Where(l => l.Title.ToLower().Contains(search) || l.Description.ToLower().Contains(search));
        if (city != "") q = q.Where(l => l.City.ToLower() == city);
        var owner = IdFilter("owner_id"); if (owner is not null) q = q.Where(l => l.OwnerId == owner);
        var status = Choice("status", "draft", "published", "archived"); if (status is not null) q = q.Where(l => l.Status == status);
        return PageAsync(q.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id), AdminListingResponse.From, ct);
    }
    [HttpGet("listings/{id:long}/")]
    public async Task<AdminListingResponse> Listing(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "listings.read");
        return AdminListingResponse.From(await housing.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
    [HttpGet("applications/")]
    public Task<PageResponse<AdminApplicationResponse>> Applications(CancellationToken ct)
    {
        AdminService.Require(Actor, "applications.read"); Query("student_id", "listing_id", "status");
        var q = housing.Applications.AsNoTracking(); var student = IdFilter("student_id"); var listing = IdFilter("listing_id");
        if (student is not null) q = q.Where(a => a.StudentId == student);
        if (listing is not null) q = q.Where(a => a.ListingId == listing);
        var status = Choice("status", "pending", "accepted", "rejected", "cancelled"); if (status is not null) q = q.Where(a => a.Status == status);
        return PageAsync(q.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id), AdminApplicationResponse.From, ct);
    }
    [HttpGet("applications/{id:long}/")]
    public async Task<AdminApplicationResponse> Application(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "applications.read");
        return AdminApplicationResponse.From(await housing.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
    private AdminPhotoResponse PhotoResponse(ListingPhoto p) => new(p.Id, p.ListingId, Origin + "/media/" + p.FileName, p.Caption, p.CreatedAt);
    [HttpGet("photos/")]
    public Task<PageResponse<AdminPhotoResponse>> Photos(CancellationToken ct)
    {
        AdminService.Require(Actor, "photos.read"); Query("listing_id"); var q = db.Photos.AsNoTracking(); var listing = IdFilter("listing_id");
        if (listing is not null) q = q.Where(p => p.ListingId == listing);
        return PageAsync(q.OrderBy(p => p.Id), PhotoResponse, ct);
    }
    [HttpGet("photos/{id:long}/")]
    public async Task<AdminPhotoResponse> Photo(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "photos.read");
        return PhotoResponse(await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
    [HttpGet("application-events/")]
    public Task<PageResponse<AdminEventResponse>> Events(CancellationToken ct)
    {
        AdminService.Require(Actor, "application_events.read"); Query("application_id", "actor_id"); var q = db.ApplicationEvents.AsNoTracking();
        var application = IdFilter("application_id"); var actor = IdFilter("actor_id");
        if (application is not null) q = q.Where(e => e.HousingApplicationId == application);
        if (actor is not null) q = q.Where(e => e.ActorId == actor);
        return PageAsync(q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id), AdminEventResponse.From, ct);
    }
    [HttpGet("application-events/{id:long}/")]
    public async Task<AdminEventResponse> Event(long id, CancellationToken ct)
    {
        AdminService.Require(Actor, "application_events.read");
        return AdminEventResponse.From(await db.ApplicationEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new ApiFault(404, "Не найдено."));
    }
}
