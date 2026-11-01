using Microsoft.EntityFrameworkCore;

namespace MealSeal.Api.Data;

public sealed class MealSealDbContext(DbContextOptions<MealSealDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> Users => Set<UserProfile>();
    public DbSet<VoucherRecord> Vouchers => Set<VoucherRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.UserId).HasMaxLength(36);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.Property(x => x.RestaurantId).HasMaxLength(128);
            entity.HasIndex(x => x.Email).IsUnique();
        });
        modelBuilder.Entity<VoucherRecord>(entity =>
        {
            entity.ToTable("vouchers");
            entity.HasKey(x => x.VoucherId);
            entity.Property(x => x.VoucherId).HasMaxLength(36);
            entity.Property(x => x.RecipientRef).HasMaxLength(120);
            entity.Property(x => x.RestaurantId).HasMaxLength(128);
            entity.Property(x => x.MealType).HasMaxLength(20);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Algorithm).HasMaxLength(20);
            entity.Property(x => x.KeyId).HasMaxLength(80);
            entity.Property(x => x.Signature).HasMaxLength(256);
            entity.Property(x => x.PayloadSha256).HasMaxLength(64);
            entity.Property(x => x.PayloadJson).HasColumnType("text");
            entity.Property(x => x.QrPng).HasColumnType("bytea");
            entity.HasIndex(x => new { x.Status, x.ExpiresAt });
        });
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.VoucherId).HasMaxLength(36);
            entity.Property(x => x.ActorUid).HasMaxLength(128);
            entity.Property(x => x.Action).HasMaxLength(32);
            entity.Property(x => x.Outcome).HasMaxLength(32);
            entity.HasIndex(x => x.VoucherId);
        });
    }
}

public sealed class UserProfile
{
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public string? RestaurantId { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class VoucherRecord
{
    public required string VoucherId { get; set; }
    public required string RecipientRef { get; set; }
    public required string RestaurantId { get; set; }
    public required string MealType { get; set; }
    public int Quantity { get; set; }
    public long IssuedAt { get; set; }
    public long ExpiresAt { get; set; }
    public required string IssuerUid { get; set; }
    public required string Status { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadSha256 { get; set; }
    public required string Signature { get; set; }
    public required string Algorithm { get; set; }
    public required string KeyId { get; set; }
    public string? RedeemedByUid { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
    public string? RevokedByUid { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The voucher's QR code, rendered once at creation time and stored so it never needs
    /// to be regenerated. Nullable only because vouchers created before this column existed have none.</summary>
    public byte[]? QrPng { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public required string VoucherId { get; set; }
    public required string ActorUid { get; set; }
    public required string Action { get; set; }
    public required string Outcome { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
