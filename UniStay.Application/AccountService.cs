using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniStay.Core;

namespace UniStay.Application;

public interface IAccountMail
{
    Task DeliverAsync(string email, string kind, string token, long accountId, CancellationToken ct);
}
public sealed class AccountService(UniStayDb db, IPasswordHasher<Account> hasher, IAccountMail mail)
{
    public static string Digest(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    public static void ValidatePassword(string password, Account account) => PasswordPolicy.Validate(password, account);
    public async Task<AccountResponse> RegisterAsync(RegisterRequest input, CancellationToken ct)
    {
        var account = new Account { Email = Normalize(input.Email), Name = input.Name.Trim(), Role = input.Role };
        if (account.Name.Length == 0) throw new ApiFault(400, new { name = new[] { "Обязательное поле." } });
        ValidatePassword(input.Password, account);
        if (await db.Accounts.AnyAsync(a => a.Email == account.Email, ct))
            throw new ApiFault(400, new { email = new[] { "Этот email уже зарегистрирован." } });
        account.PasswordHash = hasher.HashPassword(account, input.Password);
        db.Accounts.Add(account);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        { throw new ApiFault(409, "Этот email уже зарегистрирован."); }
        await IssueMailAsync(account, "verify", ct);
        return AccountResponse.From(account);
    }
    public async Task<LoginResponse> LoginAsync(LoginRequest input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var email = Normalize(input.Email);
        var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Email\"={email} FOR UPDATE").Include(a => a.Profile).SingleOrDefaultAsync(ct);
        if (account is null || !account.IsActive || hasher.VerifyHashedPassword(account, account.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            throw new ApiFault(401, "Неверный email или пароль.");
        var token = NewToken();
        var expires = DateTimeOffset.UtcNow.AddHours(24);
        db.Sessions.Add(new Session { AccountId = account.Id, Digest = Digest(token), ExpiresAt = expires });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(token, expires, AccountResponse.From(account));
    }
    public async Task<Session?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (token.Length > 256) return null;
        var digest = Digest(token);
        return await db.Sessions.AsNoTracking().Include(s => s.Account).ThenInclude(a => a.Profile)
            .SingleOrDefaultAsync(s => s.Digest == digest && s.ExpiresAt > DateTimeOffset.UtcNow && s.Account.IsActive, ct);
    }
    public async Task LogoutAsync(long sessionId, CancellationToken ct) =>
        _ = await db.Sessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);
    public async Task<ProfileResponse> PatchProfileAsync(long id, ProfilePatch patch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var account = await LockAccountAsync(id, ct);
        var p = account.Profile;
        if ((patch.HasUniversity && patch.University is null) || (patch.HasBio && patch.Bio is null) ||
            (patch.HasSchedule && patch.Schedule is null) || (patch.HasCleanliness && patch.Cleanliness is null) || (patch.HasShare && patch.ShareWithGroup is null))
            throw new ApiFault(400, "Null допустим только для smoking.");
        if (patch.HasUniversity) p.University = patch.University!.Trim();
        if (patch.HasBio) p.Bio = patch.Bio!.Trim();
        if (patch.HasSchedule) p.Schedule = patch.Schedule!;
        if (patch.HasSmoking) p.Smoking = patch.Smoking;
        if (patch.HasCleanliness) p.Cleanliness = patch.Cleanliness!;
        if (patch.HasShare) p.ShareWithGroup = patch.ShareWithGroup!.Value;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ProfileResponse.From(p);
    }
    private async Task<Account> LockAccountAsync(long id, CancellationToken ct) =>
        await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Id\"={id} FOR UPDATE")
            .Include(a => a.Profile).SingleOrDefaultAsync(ct) ?? throw new ApiFault(400, "Аккаунт недоступен.");
    public async Task RequestMailAsync(string email, string kind, CancellationToken ct)
    {
        var normalized = Normalize(email);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Email == normalized && a.IsActive, ct);
        if (account is not null && (kind == "reset" || !account.EmailVerified))
            await IssueMailAsync(account, kind, ct);
    }
    private async Task IssueMailAsync(Account account, string kind, CancellationToken ct)
    {
        var token = NewToken();
        db.AccountTokens.Add(new AccountToken
        {
            AccountId = account.Id,
            Kind = kind,
            Digest = Digest(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(kind == "verify" ? 24 : 1)
        });
        await db.SaveChangesAsync(ct);
        await mail.DeliverAsync(account.Email, kind, token, account.Id, ct);
    }
    public async Task VerifyAsync(string token, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var digest = Digest(token);
        var entry = await db.AccountTokens.SingleOrDefaultAsync(t => t.Digest == digest && t.Kind == "verify", ct)
            ?? throw new ApiFault(400, "Ссылка недействительна или истекла.");
        var account = await LockAccountAsync(entry.AccountId, ct);
        if (!account.IsActive || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ApiFault(400, "Ссылка недействительна или истекла.");
        account.EmailVerified = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task ResetAsync(ResetRequest request, CancellationToken ct)
    {
        long id;
        try { id = long.Parse(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Uid))); }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        { throw new ApiFault(400, "Ссылка недействительна или истекла."); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var account = await LockAccountAsync(id, ct);
        var digest = Digest(request.Token);
        var valid = await db.AccountTokens.AnyAsync(t => t.AccountId == id && t.Kind == "reset" && t.Digest == digest && t.ExpiresAt > DateTimeOffset.UtcNow, ct);
        if (!account.IsActive || !valid) throw new ApiFault(400, "Ссылка недействительна или истекла.");
        ValidatePassword(request.Password, account);
        account.PasswordHash = hasher.HashPassword(account, request.Password);
        await db.Sessions.Where(s => s.AccountId == id).ExecuteDeleteAsync(ct);
        await db.AccountTokens.Where(t => t.AccountId == id && t.Kind == "reset").ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
