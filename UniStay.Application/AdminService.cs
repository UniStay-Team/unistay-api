using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using UniStay.Core;
namespace UniStay.Application;

public sealed record AdminUserResponse(long Id, string Email, string Name, string Role, bool EmailVerified, bool IsActive, bool IsAdmin, DateTimeOffset DateJoined)
{
    public static AdminUserResponse From(Account a) => new(a.Id, a.Email, a.Name, a.Role, a.EmailVerified, a.IsActive, a.IsStaff, a.DateJoined);
}
public sealed record AdminMeResponse(long Id, string Email, string Name, string[] Permissions);
public sealed record AdminProfileResponse(long UserId, string University, string Bio, string Schedule, bool? Smoking, string Cleanliness, bool ShareWithGroup)
{
    public static AdminProfileResponse From(StudentProfile p) => new(p.AccountId, p.University, p.Bio, p.Schedule, p.Smoking, p.Cleanliness, p.ShareWithGroup);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminUserPatch
{
    public Optional<string?> Name { get; set; }
    public Optional<bool?> IsActive { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AdminProfilePatch
{
    public Optional<string?> University { get; set; }
    public Optional<string?> Bio { get; set; }
    public Optional<string?> Schedule { get; set; }
    public Optional<bool?> Smoking { get; set; }
    public Optional<string?> Cleanliness { get; set; }
}
public sealed class AdminService(UniStayDb db)
{
    public static readonly string[] KnownPermissions = ["users.read", "users.update", "profiles.read", "profiles.update", "listings.read", "photos.read", "applications.read", "application_events.read"];
    public static void Require(Account actor, string permission)
    {
        if (!actor.IsStaff || !actor.IsActive || !actor.EmailVerified || !actor.IsSuperuser && !actor.Permissions.Contains(permission))
            throw new ApiFault(403, "Недостаточно административных прав.");
    }
    private void Audit(Account actor, long target, string action, string requestId, Dictionary<string, object?> before, Dictionary<string, object?> after)
    {
        if (before.Count == 0) return;
        db.AdminAuditEvents.Add(new()
        {
            ActorId = actor.Id,
            TargetUserId = target,
            Action = action,
            RequestId = requestId,
            ChangedFields = before.Keys.ToArray(),
            Before = JsonSerializer.Serialize(before),
            After = JsonSerializer.Serialize(after)
        });
    }
    private static ApiFault Invalid(string field) => new(400, new Dictionary<string, string[]> { [field] = ["Некорректное значение."] });
    public async Task<AdminUserResponse> UpdateAsync(Account actor, long id, AdminUserPatch patch, string requestId, CancellationToken ct)
    {
        Require(actor, "users.update");
        if (!patch.Name.IsSet && !patch.IsActive.IsSet) throw new ApiFault(400, "Пустой PATCH.");
        if (patch.Name.IsSet && (string.IsNullOrWhiteSpace(patch.Name.Value) || patch.Name.Value.Trim().Length > 120)) throw Invalid("name");
        if (patch.IsActive.IsSet && patch.IsActive.Value is null) throw Invalid("is_active");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new ApiFault(404, "Не найдено.");
        if (patch.IsActive.IsSet && user.IsStaff) throw new ApiFault(409, "Нельзя изменять блокировку администратора.");
        var before = new Dictionary<string, object?>(); var after = new Dictionary<string, object?>();
        if (patch.Name.IsSet && user.Name != patch.Name.Value!.Trim())
        { before["name"] = user.Name; user.Name = patch.Name.Value.Trim(); after["name"] = user.Name; }
        if (patch.IsActive.IsSet && user.IsActive != patch.IsActive.Value)
        {
            before["is_active"] = user.IsActive; user.IsActive = patch.IsActive.Value!.Value; after["is_active"] = user.IsActive;
            if (!user.IsActive) await db.Sessions.Where(s => s.AccountId == id).ExecuteDeleteAsync(ct);
        }
        Audit(actor, id, "user.updated", requestId, before, after);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return AdminUserResponse.From(user);
    }
    public async Task<AdminProfileResponse> UpdateProfileAsync(Account actor, long id, AdminProfilePatch patch, string requestId, CancellationToken ct)
    {
        Require(actor, "profiles.update");
        if (!patch.University.IsSet && !patch.Bio.IsSet && !patch.Schedule.IsSet && !patch.Smoking.IsSet && !patch.Cleanliness.IsSet) throw new ApiFault(400, "Пустой PATCH.");
        if (patch.University.IsSet && (patch.University.Value is null || patch.University.Value.Length > 160)) throw Invalid("university");
        if (patch.Bio.IsSet && (patch.Bio.Value is null || patch.Bio.Value.Length > 1000)) throw Invalid("bio");
        if (patch.Schedule.IsSet && patch.Schedule.Value is not ("" or "early" or "late")) throw Invalid("schedule");
        if (patch.Cleanliness.IsSet && patch.Cleanliness.Value is not ("" or "tidy" or "relaxed")) throw Invalid("cleanliness");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var profile = await db.Set<StudentProfile>().FromSqlInterpolated($"SELECT * FROM profiles WHERE \"AccountId\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new ApiFault(404, "Не найдено.");
        var before = new Dictionary<string, object?>(); var after = new Dictionary<string, object?>();
        void Change<T>(string field, T oldValue, T newValue, Action<T> set)
        { if (!EqualityComparer<T>.Default.Equals(oldValue, newValue)) { before[field] = oldValue; after[field] = newValue; set(newValue); } }
        if (patch.University.IsSet) Change("university", profile.University, patch.University.Value!, v => profile.University = v);
        if (patch.Bio.IsSet) Change("bio", profile.Bio, patch.Bio.Value!, v => profile.Bio = v);
        if (patch.Schedule.IsSet) Change("schedule", profile.Schedule, patch.Schedule.Value!, v => profile.Schedule = v);
        if (patch.Smoking.IsSet) Change("smoking", profile.Smoking, patch.Smoking.Value, v => profile.Smoking = v);
        if (patch.Cleanliness.IsSet) Change("cleanliness", profile.Cleanliness, patch.Cleanliness.Value!, v => profile.Cleanliness = v);
        Audit(actor, id, "profile.updated", requestId, before, after);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return AdminProfileResponse.From(profile);
    }
}
