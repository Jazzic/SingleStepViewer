using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SingleStepViewer.Data.Entities;

namespace SingleStepViewer.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Playlist> Playlists { get; set; } = null!;
    public DbSet<PlaylistItem> PlaylistItems { get; set; } = null!;
    public DbSet<PlaybackHistory> PlaybackHistory { get; set; } = null!;
    public DbSet<QueueState> QueueState { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure ApplicationUser relationships
        builder.Entity<ApplicationUser>()
            .HasMany(u => u.Playlists)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure Playlist relationships
        builder.Entity<Playlist>()
            .HasMany(p => p.Items)
            .WithOne(i => i.Playlist)
            .HasForeignKey(i => i.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Playlist>()
            .HasIndex(p => p.UserId);

        builder.Entity<Playlist>()
            .Property(p => p.UpdatedAt)
            .IsConcurrencyToken();

        // Configure PlaylistItem
        builder.Entity<PlaylistItem>()
            .HasIndex(i => i.Status);

        builder.Entity<PlaylistItem>()
            .HasIndex(i => new { i.PlaylistId, i.Status });

        builder.Entity<PlaylistItem>()
            .Property(i => i.UpdatedAt)
            .IsConcurrencyToken();

        // Configure PlaybackHistory relationships
        builder.Entity<PlaybackHistory>()
            .HasOne(h => h.PlaylistItem)
            .WithMany()
            .HasForeignKey(h => h.PlaylistItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PlaybackHistory>()
            .HasOne(h => h.User)
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PlaybackHistory>()
            .HasIndex(h => h.PlayedAt);

        builder.Entity<PlaybackHistory>()
            .HasIndex(h => h.UserId);

        // Configure QueueState
        builder.Entity<QueueState>()
            .HasOne(q => q.CurrentPlaylistItem)
            .WithMany()
            .HasForeignKey(q => q.CurrentPlaylistItemId)
            .OnDelete(DeleteBehavior.SetNull);

        // Ensure only one QueueState record exists
        builder.Entity<QueueState>()
            .HasData(new QueueState
            {
                Id = 1,
                UpdatedAt = new DateTime(2026, 1, 23, 0, 0, 0, DateTimeKind.Utc)
            });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Playlist or PlaylistItem && e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity is Playlist playlist)
            {
                playlist.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is PlaylistItem item)
            {
                item.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
