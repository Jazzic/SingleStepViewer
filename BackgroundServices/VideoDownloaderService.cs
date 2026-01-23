using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SingleStepViewer.Components.Hubs;
using SingleStepViewer.Configuration;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.BackgroundServices;

public class VideoDownloaderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<PlaybackHub> _hubContext;
    private readonly ILogger<VideoDownloaderService> _logger;
    private readonly VideoOptions _videoOptions;
    private readonly SemaphoreSlim _downloadSemaphore;

    public VideoDownloaderService(
        IServiceProvider serviceProvider,
        IHubContext<PlaybackHub> hubContext,
        ILogger<VideoDownloaderService> logger,
        IOptions<VideoOptions> videoOptions)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
        _videoOptions = videoOptions.Value;
        _downloadSemaphore = new SemaphoreSlim(videoOptions.Value.MaxConcurrentDownloads);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Video Downloader Service starting");

        // Ensure storage directory exists
        if (!Directory.Exists(_videoOptions.StoragePath))
        {
            Directory.CreateDirectory(_videoOptions.StoragePath);
            _logger.LogInformation("Created video storage directory: {Path}", _videoOptions.StoragePath);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Get pending videos
                var pendingVideos = await context.PlaylistItems
                    .Where(i => i.Status == PlaylistItemStatus.Pending)
                    .OrderBy(i => i.CreatedAt)
                    .Take(_videoOptions.MaxConcurrentDownloads * 2) // Get a few extra for the queue
                    .ToListAsync(stoppingToken);

                if (pendingVideos.Any())
                {
                    _logger.LogInformation("Found {Count} pending videos to download", pendingVideos.Count);

                    // Process downloads concurrently (up to MaxConcurrentDownloads)
                    var downloadTasks = pendingVideos.Select(video =>
                        ProcessVideoDownloadAsync(video.Id, stoppingToken));

                    await Task.WhenAll(downloadTasks);
                }
                else
                {
                    _logger.LogDebug("No pending videos to download");
                }

                // Wait before checking again (10 seconds)
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in video downloader main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Video Downloader Service stopped");
    }

    private async Task ProcessVideoDownloadAsync(int videoId, CancellationToken stoppingToken)
    {
        // Use semaphore to limit concurrent downloads
        await _downloadSemaphore.WaitAsync(stoppingToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();
            var videoService = scope.ServiceProvider.GetRequiredService<IVideoService>();

            // Get the video item (with fresh data)
            var video = await context.PlaylistItems
                .Include(i => i.Playlist)
                .FirstOrDefaultAsync(i => i.Id == videoId, stoppingToken);

            if (video == null)
            {
                _logger.LogWarning("Video {VideoId} not found", videoId);
                return;
            }

            // Double-check status (another thread might have picked it up)
            if (video.Status != PlaylistItemStatus.Pending)
            {
                _logger.LogDebug("Video {VideoId} is no longer pending, skipping", videoId);
                return;
            }

            _logger.LogInformation("Starting download for video {VideoId}: {Url}", videoId, video.VideoUrl);

            // Update status to Downloading
            video.Status = PlaylistItemStatus.Downloading;
            await context.SaveChangesAsync(stoppingToken);

            // Broadcast status update
            await _hubContext.Clients.All.SendAsync("VideoDownloadStarted", videoId, stoppingToken);

            try
            {
                // Extract metadata if not already done
                if (string.IsNullOrEmpty(video.Title))
                {
                    _logger.LogInformation("Extracting metadata for video {VideoId}", videoId);
                    var metadata = await videoService.ExtractMetadataAsync(video.VideoUrl);

                    if (metadata != null)
                    {
                        video.Title = metadata.Title;
                        video.ThumbnailUrl = metadata.ThumbnailUrl;
                        video.Duration = metadata.Duration;
                        await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Metadata extracted for video {VideoId}: {Title}", videoId, video.Title);
                    }
                }

                // Generate output filename
                var safeFileName = GetSafeFileName(video.Title ?? $"video_{videoId}");
                var outputPath = Path.Combine(_videoOptions.StoragePath, safeFileName);

                // Download video
                _logger.LogInformation("Downloading video {VideoId} to {Path}", videoId, outputPath);
                var downloadResult = await downloadService.DownloadVideoAsync(video.VideoUrl, outputPath);

                if (downloadResult.Success && !string.IsNullOrEmpty(downloadResult.FilePath))
                {
                    // Update video as ready
                    video.Status = PlaylistItemStatus.Ready;
                    video.LocalFilePath = downloadResult.FilePath;
                    video.DownloadedAt = DateTime.UtcNow;
                    video.ErrorMessage = null;
                    await context.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation(
                        "Successfully downloaded video {VideoId} ({Title}) to {FilePath}",
                        videoId,
                        video.Title,
                        video.LocalFilePath);

                    // Broadcast success
                    await _hubContext.Clients.All.SendAsync("VideoDownloadCompleted", videoId, stoppingToken);
                    await _hubContext.Clients.All.SendAsync("QueueUpdated", stoppingToken);
                }
                else
                {
                    // Download failed
                    video.Status = PlaylistItemStatus.Error;
                    video.ErrorMessage = downloadResult.ErrorMessage ?? "Download failed";
                    await context.SaveChangesAsync(stoppingToken);

                    _logger.LogError(
                        "Failed to download video {VideoId}: {Error}",
                        videoId,
                        video.ErrorMessage);

                    // Broadcast error
                    await _hubContext.Clients.All.SendAsync(
                        "VideoDownloadFailed",
                        videoId,
                        video.ErrorMessage,
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading video {VideoId}", videoId);

                video.Status = PlaylistItemStatus.Error;
                video.ErrorMessage = $"Download error: {ex.Message}";
                await context.SaveChangesAsync(stoppingToken);

                // Broadcast error
                await _hubContext.Clients.All.SendAsync(
                    "VideoDownloadFailed",
                    videoId,
                    video.ErrorMessage,
                    stoppingToken);
            }
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private static string GetSafeFileName(string fileName)
    {
        // Remove invalid characters from filename
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        // Trim and limit length
        safe = safe.Trim().Substring(0, Math.Min(safe.Length, 100));

        // Add timestamp to ensure uniqueness
        return $"{safe}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    }
}
