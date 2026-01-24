using Microsoft.AspNetCore.Identity;

namespace SingleStepViewer.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public ICollection<Playlist> Playlists { get; set; } = new List<Playlist>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
