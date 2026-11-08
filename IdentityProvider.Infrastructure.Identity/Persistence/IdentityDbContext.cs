using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityProvider.Infrastructure.Identity.Persistence;

public sealed class IdentityUserAccount : IdentityUser
{
    public bool Enabled { get; set; } = true;
}

public sealed class SmsChallenge
{
    public string PhoneNumber { get; set; }
    public string CodeHash { get; set; }
    public string Salt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public int Attempts { get; set; }
    public string SubjectId { get; set; }
    // Optimistic concurrency protects consumption and attempt limits across instances.
    public string Version { get; set; } = Guid.NewGuid().ToString();
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<IdentityUserAccount, IdentityRole, string>(options)
{
    public DbSet<SmsChallenge> SmsChallenges => Set<SmsChallenge>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        if (!Database.IsSqlite()) builder.HasDefaultSchema("identity");
        builder.UseOpenIddict();
        builder.Entity<IdentityUserAccount>().Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Entity<IdentityUserAccount>().HasIndex(x => x.PhoneNumber).IsUnique()
            .HasFilter("[PhoneNumber] IS NOT NULL");
        builder.Entity<SmsChallenge>(entity =>
        {
            entity.HasKey(x => x.PhoneNumber);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Salt).HasMaxLength(36).IsRequired();
            entity.Property(x => x.SubjectId).HasMaxLength(450);
            entity.Property(x => x.Version).HasMaxLength(36).IsConcurrencyToken();
        });
    }
}
