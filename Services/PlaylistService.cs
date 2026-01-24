using Microsoft.EntityFrameworkCore;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class PlaylistService : IPlaylistService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PlaylistService> _logger;

    public PlaylistService(ApplicationDbContext context, ILogger<PlaylistService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Playlist?> GetPlaylistByIdAsync(int id)
    {
        var playlist = await _context.Playlists
            .Include(p => p.Items)
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playlist?.User == null)
        {
            return null;
        }

        return playlist;
    }

    public async Task<IEnumerable<Playlist>> GetUserPlaylistsAsync(string userId)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            return Enumerable.Empty<Playlist>();
        }

        return await _context.Playlists
            .Include(p => p.Items)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Playlist> CreatePlaylistAsync(string userId, string name, string? description = null)
    {
        var playlist = new Playlist
        {
            UserId = userId,
            Name = name,
            Description = description
        };

        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created playlist {PlaylistId} for user {UserId}", playlist.Id, userId);

        return playlist;
    }

    public async Task<bool> UpdatePlaylistAsync(int id, string name, string? description = null)
    {
        var playlist = await _context.Playlists.FindAsync(id);
        if (playlist == null) return false;

        playlist.Name = name;
        playlist.Description = description;
        playlist.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated playlist {PlaylistId}", id);

        return true;
    }

    public async Task<bool> UpdateRemoveAfterPlayingAsync(int id, bool removeAfterPlaying)
    {
        var playlist = await _context.Playlists.FindAsync(id);
        if (playlist == null) return false;

        playlist.RemoveAfterPlaying = removeAfterPlaying;
        playlist.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Updated playlist {PlaylistId} RemoveAfterPlaying to {RemoveAfterPlaying}",
            id,
            removeAfterPlaying);

        return true;
    }

    public async Task<bool> DeletePlaylistAsync(int id)
    {
        var playlist = await _context.Playlists.FindAsync(id);
        if (playlist == null) return false;

        _context.Playlists.Remove(playlist);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted playlist {PlaylistId}", id);

        return true;
    }
}
