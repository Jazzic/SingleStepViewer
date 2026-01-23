using System.ComponentModel.DataAnnotations;

namespace SingleStepViewer.Data.Entities;

public class Playlist
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();

    /// <summary>
    /// When true, videos are removed from the queue after being played.
    /// When false, videos remain in the queue and can be played again.
    /// </summary>
    public bool RemoveAfterPlaying { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
