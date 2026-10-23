using Microsoft.EntityFrameworkCore;
using UniStay.Core;
namespace UniStay.Application;

public sealed class HousingService(UniStayDb db)
{
    public IQueryable<Listing> Listings => db.Listings.Include(l => l.Owner).Include(l => l.Photos).Include(l => l.Applications);
    public IQueryable<HousingApplication> Applications => db.Applications.Include(a => a.Listing).Include(a => a.Student).ThenInclude(s => s.Profile).Include(a => a.Events);
    public async Task<Listing> DetailAsync(long id, Account? actor, CancellationToken ct)
    {
        var l = await Listings.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (l is null || l.Status != "published" && l.OwnerId != actor?.Id && !l.Applications.Any(a => a.StudentId == actor?.Id && a.Status == "accepted"))
            throw new ApiFault(404, "Не найдено.");
        return l;
    }
    private async Task<Listing> LockListingAsync(long id, CancellationToken ct) =>
        await db.Listings.FromSqlInterpolated($"SELECT * FROM listings WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new ApiFault(404, "Не найдено.");
    private async Task<Account> LockStudentAsync(long id, CancellationToken ct) =>
        await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Id\"={id} FOR UPDATE").Include(a => a.Profile).SingleAsync(ct);
    private static void Text(Optional<string?> field, bool required, int max, Action<string> assign)
    {
        if (!field.IsSet) { if (required) throw new ApiFault(400, "Не заполнены обязательные поля объявления."); return; }
        if (string.IsNullOrWhiteSpace(field.Value) || field.Value.Length > max) throw new ApiFault(400, "Некорректное текстовое поле объявления.");
        assign(field.Value.Trim());
    }
    public async Task<Listing> SaveListingAsync(Account actor, long? id, ListingRequest input, CancellationToken ct)
    {
        Access.Role(actor, "landlord");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var l = id is null ? new Listing { OwnerId = actor.Id } : await LockListingAsync(id.Value, ct);
        if (l.OwnerId != actor.Id) throw new ApiFault(404, "Не найдено.");
        var occupied = id is null ? 0 : await db.Applications.CountAsync(a => a.ListingId == id && a.Status == "accepted", ct);
        if (occupied > 0 && (input.Price.IsSet && input.Price.Value != l.Price || input.Deposit.IsSet && input.Deposit.Value != l.Deposit ||
            input.UtilitiesIncluded.IsSet && input.UtilitiesIncluded.Value != l.UtilitiesIncluded || input.MoveInDate.IsSet && input.MoveInDate.Value != l.MoveInDate))
            throw new ApiFault(409, "При принятых жильцах условия аренды менять нельзя.");
        Text(input.Title, id is null, 160, v => l.Title = v);
        Text(input.Description, id is null, 5000, v => l.Description = v);
        Text(input.City, false, 100, v => l.City = v);
        Text(input.District, id is null, 100, v => l.District = v);
        Text(input.Address, id is null, 255, v => l.Address = v);
        Text(input.University, id is null, 160, v => l.University = v);
        if (input.Price.IsSet) l.Price = input.Price.Value ?? throw new ApiFault(400, "Цена обязательна.");
        if (input.Deposit.IsSet) l.Deposit = input.Deposit.Value ?? throw new ApiFault(400, "Залог не может быть null.");
        if (l.Price <= 0 || l.Price > 99999999.99m || l.Deposit < 0 || l.Deposit > 99999999.99m || decimal.Round(l.Price, 2) != l.Price || decimal.Round(l.Deposit, 2) != l.Deposit)
            throw new ApiFault(400, "Некорректная цена или залог.");
        if (input.Capacity.IsSet) l.Capacity = input.Capacity.Value ?? throw new ApiFault(400, "Вместимость обязательна.");
        if (l.Capacity is < 1 or > 6) throw new ApiFault(400, "Вместимость: от 1 до 6.");
        if (l.Capacity < occupied) throw new ApiFault(409, "Вместимость ниже числа принятых жильцов.");
        if (input.UtilitiesIncluded.IsSet) l.UtilitiesIncluded = input.UtilitiesIncluded.Value ?? throw new ApiFault(400, "Некорректное utilities_included.");
        if (input.MoveInDate.IsSet) l.MoveInDate = input.MoveInDate.Value;
        if (input.Status.IsSet)
        {
            if (input.Status.Value is not ("draft" or "published" or "archived")) throw new ApiFault(400, "Некорректный статус.");
            if (input.Status.Value == "published" && (id is null || !await db.Photos.AnyAsync(p => p.ListingId == id, ct)))
                throw new ApiFault(400, new { status = new[] { "Перед публикацией добавьте фотографию." } });
            l.Status = input.Status.Value;
        }
        l.UpdatedAt = DateTimeOffset.UtcNow;
        if (id is null) db.Listings.Add(l);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await Listings.SingleAsync(x => x.Id == l.Id, ct);
    }
    public async Task ArchiveAsync(Account actor, long id, CancellationToken ct)
    {
        Access.Role(actor, "landlord");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var l = await LockListingAsync(id, ct);
        if (l.OwnerId != actor.Id) throw new ApiFault(404, "Не найдено.");
        l.Status = "archived"; l.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<HousingApplication> ApplyAsync(Account actor, ApplyRequest request, CancellationToken ct)
    {
        Access.Role(actor, "student");
        if (request.Message is null) throw new ApiFault(400, "message не может быть null.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var student = await LockStudentAsync(actor.Id, ct);
        var l = await LockListingAsync(request.ListingId, ct);
        if (l.Status != "published") throw new ApiFault(404, "Объявление не найдено или снято с публикации.");
        var p = student.Profile;
        if (p.University.Length == 0 || p.Schedule.Length == 0 || p.Cleanliness.Length == 0 || p.Smoking is null)
            throw new ApiFault(400, "Перед заявкой заполните университет и бытовые привычки.");
        if (await db.Applications.AnyAsync(a => a.StudentId == actor.Id && (a.Status == "accepted" || a.ListingId == l.Id && a.Status == "pending"), ct))
            throw new ApiFault(409, "Уже есть активная заявка или принятое место.");
        if (await db.Applications.CountAsync(a => a.StudentId == actor.Id && a.Status == "pending", ct) >= 5)
            throw new ApiFault(409, "Можно иметь не больше пяти ожидающих заявок.");
        if (await db.Applications.CountAsync(a => a.ListingId == l.Id && a.Status == "accepted", ct) >= l.Capacity)
            throw new ApiFault(409, "Свободных мест нет.");
        var application = new HousingApplication { ListingId = l.Id, Listing = l, StudentId = actor.Id, Student = student, Message = request.Message.Trim() };
        application.Events.Add(new ApplicationEvent { ActorId = actor.Id, ToStatus = "pending" });
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return application;
    }
    public async Task<HousingApplication> TransitionAsync(Account actor, long id, string target, CancellationToken ct)
    {
        var info = await db.Applications.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.StudentId, a.ListingId }).SingleOrDefaultAsync(ct)
            ?? throw new ApiFault(404, "Не найдено.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var student = await LockStudentAsync(info.StudentId, ct);
        var l = await LockListingAsync(info.ListingId, ct);
        var application = await db.Applications.FromSqlInterpolated($"SELECT * FROM applications WHERE \"Id\"={id} FOR UPDATE").SingleAsync(ct);
        if (target == "cancelled") { if (actor.Id != info.StudentId) throw new ApiFault(404, "Не найдено."); }
        else { if (actor.Id != l.OwnerId) throw new ApiFault(404, "Не найдено."); Access.Role(actor, "landlord"); }
        if (application.Status != target)
        {
            if (target == "cancelled" ? application.Status is not ("pending" or "accepted") : application.Status != "pending")
                throw new ApiFault(409, "Недопустимый переход статуса.");
            if (target == "accepted")
            {
                if (l.Status != "published" || !student.IsActive || !student.EmailVerified) throw new ApiFault(409, "Объявление или студент недоступны.");
                if (await db.Applications.AnyAsync(a => a.StudentId == student.Id && a.Status == "accepted", ct)) throw new ApiFault(409, "У студента уже есть другое место.");
                if (await db.Applications.CountAsync(a => a.ListingId == l.Id && a.Status == "accepted", ct) >= l.Capacity) throw new ApiFault(409, "Последнее место занято.");
            }
            application.Events.Add(new ApplicationEvent { ActorId = actor.Id, FromStatus = application.Status, ToStatus = target });
            application.Status = target; application.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return await Applications.AsNoTracking().SingleAsync(a => a.Id == id, ct);
    }
    public async Task<IReadOnlyList<GroupResponse>> GroupAsync(Account actor, long listingId, CancellationToken ct)
    {
        Access.Verified(actor);
        var l = await DetailAsync(listingId, actor, ct);
        var accepted = await Applications.Where(a => a.ListingId == listingId && a.Status == "accepted").ToListAsync(ct);
        var result = new List<GroupResponse>();
        foreach (var a in accepted)
        {
            var p = a.Student.Profile; var me = actor.Profile;
            if (!p.ShareWithGroup && l.OwnerId != actor.Id && a.StudentId != actor.Id) continue;
            var compared = 0; var matches = new List<string>();
            void Compare<T>(string key, T value, T mine) { if (value is null || mine is null || value.Equals("") || mine.Equals("")) return; compared++; if (value.Equals(mine)) matches.Add(key); }
            Compare("schedule", p.Schedule, me.Schedule); Compare("smoking", p.Smoking, me.Smoking); Compare("cleanliness", p.Cleanliness, me.Cleanliness);
            int? score = compared > 0 && actor.Role == "student" && actor.Id != a.StudentId ? (int)Math.Round(100.0 * matches.Count / compared) : null;
            result.Add(new(a.StudentId, a.Student.Name, p.University, p.Bio, p.Schedule, p.Smoking, p.Cleanliness, score, matches, compared));
        }
        return result;
    }
}
