using System.ComponentModel.DataAnnotations;

namespace SingleStepViewer.Data.Entities;

public class PlaybackHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int PlaylistItemId { get; set; }

    public PlaylistItem PlaylistItem { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;

    public TimeSpan? PlaybackDuration { get; set; }

    public bool CompletedSuccessfully { get; set; } = true;

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
