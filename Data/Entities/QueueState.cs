using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SingleStepViewer.Data.Entities;

public class QueueState
{
    [Key]
    public int Id { get; set; }

    public int? CurrentPlaylistItemId { get; set; }

    public PlaylistItem? CurrentPlaylistItem { get; set; }

    public DateTime? CurrentVideoStartedAt { get; set; }

    public PlaybackStatus Status { get; set; } = PlaybackStatus.Idle;

    // JSON serialized dictionary of userId -> lastPlayedTime
    [MaxLength(4000)]
    public string LastUserPlayedJson { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Helper methods to work with the JSON dictionary
    public Dictionary<string, DateTime> GetLastUserPlayed()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(LastUserPlayedJson)
                   ?? new Dictionary<string, DateTime>();
        }
        catch
        {
            return new Dictionary<string, DateTime>();
        }
    }

    public void SetLastUserPlayed(Dictionary<string, DateTime> data)
    {
        LastUserPlayedJson = JsonSerializer.Serialize(data);
    }

    public void UpdateUserPlayedTime(string userId, DateTime time)
    {
        var dict = GetLastUserPlayed();
        dict[userId] = time;
        SetLastUserPlayed(dict);
    }
}

public enum PlaybackStatus
{
    Idle,       // No video playing
    Playing,    // Video is playing
    Paused,     // Video is paused
    Error       // Playback error
}
