using Microsoft.EntityFrameworkCore;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class PlaylistItemService : IPlaylistItemService
{
    private readonly ApplicationDbContext _context;
    private readonly IVideoService _videoService;
    private readonly ILogger<PlaylistItemService> _logger;

    public PlaylistItemService(
        ApplicationDbContext context,
        IVideoService videoService,
        ILogger<PlaylistItemService> logger)
    {
        _context = context;
        _videoService = videoService;
        _logger = logger;
    }

    public async Task<PlaylistItem?> GetByIdAsync(int id)
    {
        return await _context.PlaylistItems
            .Include(i => i.Playlist)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<IEnumerable<PlaylistItem>> GetByPlaylistIdAsync(int playlistId)
    {
        return await _context.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .OrderByDescending(i => i.Priority)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<PlaylistItem> AddVideoAsync(int playlistId, string videoUrl, int priority = 5)
    {
        _logger.LogInformation("Adding video {VideoUrl} to playlist {PlaylistId}", videoUrl, playlistId);

        // Create playlist item with pending status
        var item = new PlaylistItem
        {
            PlaylistId = playlistId,
            VideoUrl = videoUrl,
            Priority = priority,
            Status = PlaylistItemStatus.Pending
        };

        _context.PlaylistItems.Add(item);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created playlist item {ItemId} - video will be downloaded in background", item.Id);

        // Try to extract metadata (non-blocking) - use proper task handling
        var metadataTask = Task.Run(async () =>
        {
            try
            {
                var metadata = await _videoService.ExtractMetadataAsync(videoUrl);
                if (metadata != null)
                {
                    var itemToUpdate = await _context.PlaylistItems.FindAsync(item.Id);
                    if (itemToUpdate != null)
                    {
                        itemToUpdate.Title = metadata.Title;
                        itemToUpdate.ThumbnailUrl = metadata.ThumbnailUrl;
                        itemToUpdate.Duration = metadata.Duration;
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract metadata for item {ItemId}", item.Id);
            }
        });

        // Don't wait for metadata extraction, but log any unhandled exceptions
        _ = metadataTask.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                _logger.LogError(t.Exception.GetBaseException(), "Unhandled exception in metadata extraction for item {ItemId}", item.Id);
            }
        }, TaskScheduler.Default);

        return item;
    }

    public async Task<bool> UpdatePriorityAsync(int id, int priority)
    {
        var item = await _context.PlaylistItems.FindAsync(id);
        if (item == null) return false;

        item.Priority = Math.Clamp(priority, 1, 10);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated priority for item {ItemId} to {Priority}", id, item.Priority);

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.PlaylistItems.FindAsync(id);
        if (item == null) return false;

        // Delete the file if it exists
        if (!string.IsNullOrEmpty(item.LocalFilePath) && File.Exists(item.LocalFilePath))
        {
            try
            {
                File.Delete(item.LocalFilePath);
                _logger.LogInformation("Deleted video file {FilePath}", item.LocalFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete video file {FilePath}", item.LocalFilePath);
            }
        }

        _context.PlaylistItems.Remove(item);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted playlist item {ItemId}", id);

        return true;
    }

    public async Task<bool> RequeueAsync(int id)
    {
        var item = await _context.PlaylistItems.FindAsync(id);
        if (item == null) return false;

        // Only allow re-queueing of played or error videos
        if (item.Status != PlaylistItemStatus.Played && item.Status != PlaylistItemStatus.Error)
        {
            _logger.LogWarning("Cannot re-queue item {ItemId} with status {Status}", id, item.Status);
            return false;
        }

        // Check if the video file still exists
        if (string.IsNullOrEmpty(item.LocalFilePath) || !File.Exists(item.LocalFilePath))
        {
            _logger.LogWarning("Cannot re-queue item {ItemId} - video file not found", id);
            return false;
        }

        item.Status = PlaylistItemStatus.Ready;
        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Re-queued playlist item {ItemId}", id);

        return true;
    }

    public async Task<int> GetPendingCountAsync()
    {
        return await _context.PlaylistItems.CountAsync(i => i.Status == PlaylistItemStatus.Pending);
    }

    public async Task<int> GetReadyCountAsync()
    {
        return await _context.PlaylistItems.CountAsync(i => i.Status == PlaylistItemStatus.Ready);
    }

    public async Task<int> GetPlayedCountAsync()
    {
        return await _context.PlaylistItems.CountAsync(i => i.Status == PlaylistItemStatus.Played);
    }

    public async Task<IEnumerable<PlaylistItem>> GetPlayedVideosAsync(int limit = 100)
    {
        return await _context.PlaylistItems
            .Include(i => i.Playlist)
            .ThenInclude(p => p.User)
            .Where(i => i.Status == PlaylistItemStatus.Played)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
