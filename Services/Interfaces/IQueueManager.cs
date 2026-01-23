using SingleStepViewer.Data.Entities;

namespace SingleStepViewer.Services.Interfaces;

public interface IQueueManager
{
    Task<PlaylistItem?> GetNextVideoAsync();
    Task<IEnumerable<PlaylistItem>> GetUpcomingVideosAsync(int count = 10);
    Task MarkVideoAsPlayedAsync(int playlistItemId, string userId, bool success = true, string? errorMessage = null);
}
