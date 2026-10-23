using ImageMagick;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniStay.Application;
using UniStay.Core;
namespace UniStay.Api;

public static class DemoSeed
{
    public static async Task RunAsync(UniStayDb db, IPasswordHasher<Account> hasher, string password, string media)
    {
        PasswordPolicy.Validate(password, new Account { Email = "demo@example.test" });
        // The caller must enforce the exact database name and explicit DemoMode before entry.
        using var picture = new MagickImage(MagickColors.LightSteelBlue, 640, 400);
        picture.Format = MagickFormat.Jpeg;
        Directory.CreateDirectory(media);
        await picture.WriteAsync(Path.Combine(media, "synthetic-demo.jpg"));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE admin_audit_events, application_events, applications, photos, listings, account_tokens, sessions, profiles, accounts RESTART IDENTITY CASCADE");
        Account User(string email, string name, string role, bool admin = false) => new()
        {
            Email = email + "@example.test",
            Name = name,
            Role = role,
            EmailVerified = true,
            IsStaff = admin,
            Permissions = admin ? ["users.read", "listings.read", "applications.read"] : [],
            Profile = new() { University = "AITU", Schedule = "early", Smoking = false, Cleanliness = "tidy", ShareWithGroup = true }
        };
        var student = User("demo-student", "Демо студент", "student");
        var neighbor = User("demo-neighbor", "Демо сосед", "student");
        var owner = User("demo-owner", "Демо владелец", "landlord");
        var admin = User("demo-admin", "Демо администратор", "student", true);
        foreach (var user in new[] { student, neighbor, owner, admin })
        { user.PasswordHash = hasher.HashPassword(user, password); db.Accounts.Add(user); }
        await db.SaveChangesAsync();
        for (var i = 0; i < 4; i++)
        {
            var listing = new Listing
            {
                OwnerId = owner.Id,
                Title = new[] { "Демо: место рядом с AITU", "Демо: свободная квартира", "Демо: черновик", "Демо: архив" }[i],
                Description = "Искусственное объявление для проверки дипломного проекта. Изображение — тестовая заглушка.",
                City = "Астана",
                District = "Есиль",
                Address = "Тестовый адрес " + (i + 1),
                University = "AITU",
                Price = 85000 + i * 5000,
                Deposit = 20000,
                Capacity = 2,
                Status = i == 2 ? "draft" : i == 3 ? "archived" : "published",
                Photos = [new() { FileName = "synthetic-demo.jpg", Caption = "Синтетическое изображение" }]
            };
            db.Listings.Add(listing); await db.SaveChangesAsync();
            if (i == 0)
            {
                db.Applications.Add(new()
                {
                    ListingId = listing.Id,
                    StudentId = neighbor.Id,
                    Status = "accepted",
                    Events = [new() { ActorId = neighbor.Id, ToStatus = "pending" }, new() { ActorId = owner.Id, FromStatus = "pending", ToStatus = "accepted" }]
                });
            }
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
