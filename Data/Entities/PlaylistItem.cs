using System.ComponentModel.DataAnnotations;

namespace SingleStepViewer.Data.Entities;

public class PlaylistItem
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int PlaylistId { get; set; }

    public Playlist Playlist { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string VideoUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? ThumbnailUrl { get; set; }

    public TimeSpan? Duration { get; set; }

    [Range(1, 10)]
    public int Priority { get; set; } = 5;

    public PlaylistItemStatus Status { get; set; } = PlaylistItemStatus.Pending;

    [MaxLength(2000)]
    public string? LocalFilePath { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DownloadedAt { get; set; }
}

public enum PlaylistItemStatus
{
    Pending,    // Waiting to be downloaded
    Downloading,// Currently downloading
    Ready,      // Downloaded and ready to play
    Playing,    // Currently playing
    Played,     // Already played
    Error       // Download or playback error
}
