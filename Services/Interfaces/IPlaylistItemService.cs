using SingleStepViewer.Data.Entities;

namespace SingleStepViewer.Services.Interfaces;

public interface IPlaylistItemService
{
    Task<PlaylistItem?> GetByIdAsync(int id);
    Task<IEnumerable<PlaylistItem>> GetByPlaylistIdAsync(int playlistId);
    Task<PlaylistItem> AddVideoAsync(int playlistId, string videoUrl, int priority = 5);
    Task<bool> UpdatePriorityAsync(int id, int priority);
    Task<bool> DeleteAsync(int id);
    Task<bool> RequeueAsync(int id);
    Task<int> GetPendingCountAsync();
    Task<int> GetReadyCountAsync();
    Task<int> GetPlayedCountAsync();
    Task<IEnumerable<PlaylistItem>> GetPlayedVideosAsync(int limit = 100);
}
