using Microsoft.EntityFrameworkCore;

namespace UniStay.Core;

public sealed class Account
{
    public long Id { get; set; }
    public DateTimeOffset DateJoined { get; set; } = DateTimeOffset.UtcNow;
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "student";
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsStaff { get; set; }
    public bool IsSuperuser { get; set; }
    public string[] Permissions { get; set; } = [];
    public StudentProfile Profile { get; set; } = new();
}
public sealed class StudentProfile
{
    public long AccountId { get; set; }
    public string University { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Schedule { get; set; } = "";
    public bool? Smoking { get; set; }
    public string Cleanliness { get; set; } = "";
    public bool ShareWithGroup { get; set; }
}
public sealed class Session
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class AccountToken
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Kind { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class UniStayDb(DbContextOptions<UniStayDb> options) : DbContext(options)
{
    public DbSet<AdminAuditEvent> AdminAuditEvents => Set<AdminAuditEvent>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingPhoto> Photos => Set<ListingPhoto>();
    public DbSet<HousingApplication> Applications => Set<HousingApplication>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AdminAuditEvent>(e =>
        {
            e.ToTable("admin_audit_events");
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Listing>(e =>
        {
            e.ToTable("listings", t =>
            {
                t.HasCheckConstraint("valid_capacity", "\"Capacity\" BETWEEN 1 AND 6");
                t.HasCheckConstraint("valid_prices", "\"Price\" > 0 AND \"Deposit\" >= 0");
                t.HasCheckConstraint("listing_status", "\"Status\" IN ('draft','published','archived')");
            });
            e.Property(x => x.Price).HasPrecision(10, 2); e.Property(x => x.Deposit).HasPrecision(10, 2);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Photos).WithOne().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.City }); e.HasIndex(x => x.Price);
        });
        model.Entity<ListingPhoto>().ToTable("photos");
        model.Entity<HousingApplication>(e =>
        {
            e.ToTable("applications", t => t.HasCheckConstraint("application_status", "\"Status\" IN ('pending','accepted','rejected','cancelled')"));
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Listing).WithMany(x => x.Applications).HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StudentId).IsUnique().HasFilter("\"Status\" = 'accepted'");
            e.HasIndex(x => new { x.StudentId, x.ListingId }).IsUnique().HasFilter("\"Status\" IN ('pending','accepted')");
            e.HasIndex(x => new { x.ListingId, x.Status });
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.HousingApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ApplicationEvent>(e =>
        {
            e.ToTable("application_events");
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Account>(e =>
        {
            e.ToTable("accounts", t => t.HasCheckConstraint("account_role", "\"Role\" IN ('student','landlord')"));
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Role).HasMaxLength(10);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<StudentProfile>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<StudentProfile>(e =>
        {
            e.ToTable("profiles"); e.HasKey(x => x.AccountId);
            e.Property(x => x.University).HasMaxLength(160);
            e.Property(x => x.Bio).HasMaxLength(1000);
            e.Property(x => x.Schedule).HasMaxLength(10);
            e.Property(x => x.Cleanliness).HasMaxLength(10);
        });
        model.Entity<Session>(e =>
        {
            e.ToTable("sessions"); e.HasIndex(x => x.Digest).IsUnique();
            e.Property(x => x.Digest).HasMaxLength(64);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AccountToken>(e =>
        {
            e.ToTable("account_tokens"); e.HasIndex(x => x.Digest).IsUnique();
            e.Property(x => x.Digest).HasMaxLength(64);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class AdminAuditEvent
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public long TargetUserId { get; set; }
    public string Action { get; set; } = "";
    public string[] ChangedFields { get; set; } = [];
    public string Before { get; set; } = "{}";
    public string After { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RequestId { get; set; } = "";
}
