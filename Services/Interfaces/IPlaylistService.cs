using SingleStepViewer.Data.Entities;

namespace SingleStepViewer.Services.Interfaces;

public interface IPlaylistService
{
    Task<Playlist?> GetPlaylistByIdAsync(int id);
    Task<IEnumerable<Playlist>> GetUserPlaylistsAsync(string userId);
    Task<Playlist> CreatePlaylistAsync(string userId, string name, string? description = null);
    Task<bool> UpdatePlaylistAsync(int id, string name, string? description = null);
    Task<bool> UpdateRemoveAfterPlayingAsync(int id, bool removeAfterPlaying);
    Task<bool> DeletePlaylistAsync(int id);
}
