using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SingleStepViewer.Components.Hubs;
using SingleStepViewer.Configuration;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.BackgroundServices;

public class PlaybackEngineService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlaybackService _playbackService;
    private readonly IHubContext<PlaybackHub> _hubContext;
    private readonly ILogger<PlaybackEngineService> _logger;
    private readonly SchedulingOptions _schedulingOptions;
    private PlaylistItem? _currentVideo;

    public PlaybackEngineService(
        IServiceProvider serviceProvider,
        IPlaybackService playbackService,
        IHubContext<PlaybackHub> hubContext,
        ILogger<PlaybackEngineService> logger,
        IOptions<SchedulingOptions> schedulingOptions)
    {
        _serviceProvider = serviceProvider;
        _playbackService = playbackService;
        _hubContext = hubContext;
        _logger = logger;
        _schedulingOptions = schedulingOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Playback Engine Service starting");

        try
        {
            // Initialize VLC
            await _playbackService.InitializeAsync();

            // Subscribe to playback events
            _playbackService.MediaEnded += OnMediaEnded;
            _playbackService.MediaError += OnMediaError;

            _logger.LogInformation("Playback service initialized, starting main loop");

            // Reset any videos stuck in "Playing" status from previous sessions
            await ResetStuckVideosAsync();

            // Immediately check for videos to play on startup
            await CheckAndPlayNextVideoAsync();

            // Main playback loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // If no video is currently playing, get the next one
                    if (!_playbackService.IsPlaying && _currentVideo == null)
                    {
                        await CheckAndPlayNextVideoAsync();
                    }

                    // Wait before checking again
                    await Task.Delay(
                        TimeSpan.FromSeconds(_schedulingOptions.QueueCheckIntervalSeconds),
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in playback engine main loop");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in playback engine");
        }
        finally
        {
            _playbackService.MediaEnded -= OnMediaEnded;
            _playbackService.MediaError -= OnMediaError;
            _logger.LogInformation("Playback Engine Service stopped");
        }
    }

    private async Task CheckAndPlayNextVideoAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var queueManager = scope.ServiceProvider.GetRequiredService<IQueueManager>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Get next video from queue
            var nextVideo = await queueManager.GetNextVideoAsync();

            if (nextVideo == null)
            {
                _logger.LogDebug("No videos in queue");
                return;
            }

            if (string.IsNullOrEmpty(nextVideo.LocalFilePath) || !File.Exists(nextVideo.LocalFilePath))
            {
                _logger.LogWarning(
                    "Video {VideoId} has no valid file path, marking as error",
                    nextVideo.Id);

                nextVideo.Status = PlaylistItemStatus.Error;
                nextVideo.ErrorMessage = "Video file not found";
                await context.SaveChangesAsync();
                return;
            }

            _logger.LogInformation(
                "Starting playback of video {VideoId}: {Title} from user {UserId}",
                nextVideo.Id,
                nextVideo.Title,
                nextVideo.Playlist.UserId);

            // Update video status to Playing
            nextVideo.Status = PlaylistItemStatus.Playing;
            await context.SaveChangesAsync();

            // Update queue state
            var queueState = await context.QueueState.FirstOrDefaultAsync();
            if (queueState != null)
            {
                queueState.CurrentPlaylistItemId = nextVideo.Id;
                queueState.CurrentVideoStartedAt = DateTime.UtcNow;
                queueState.Status = PlaybackStatus.Playing;
                queueState.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            // Broadcast to all clients
            _logger.LogInformation("Broadcasting VideoStarted event for video {VideoId}", nextVideo.Id);
            await _hubContext.Clients.All.SendAsync(
                "VideoStarted",
                nextVideo.Id,
                nextVideo.Title ?? "Unknown",
                nextVideo.Playlist.User.UserName ?? "Unknown User");

            // Also send QueueUpdated to refresh the entire queue display
            _logger.LogInformation("Broadcasting QueueUpdated event");
            await _hubContext.Clients.All.SendAsync("QueueUpdated");

            // Start playback
            _currentVideo = nextVideo;
            await _playbackService.PlayAsync(nextVideo.LocalFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking and playing next video");
        }
    }

    private async void OnMediaEnded(object? sender, EventArgs e)
    {
        try
        {
            if (_currentVideo == null)
            {
                _logger.LogWarning("Media ended but no current video tracked");
                return;
            }

            _logger.LogInformation(
                "Video {VideoId} playback completed successfully",
                _currentVideo.Id);

            using var scope = _serviceProvider.CreateScope();
            var queueManager = scope.ServiceProvider.GetRequiredService<IQueueManager>();

            // Mark as played
            await queueManager.MarkVideoAsPlayedAsync(
                _currentVideo.Id,
                _currentVideo.Playlist.UserId,
                success: true);

            // Broadcast to all clients
            _logger.LogInformation("Broadcasting VideoEnded event for video {VideoId}", _currentVideo.Id);
            await _hubContext.Clients.All.SendAsync("VideoEnded", _currentVideo.Id);
            _logger.LogInformation("Broadcasting QueueUpdated event after video ended");
            await _hubContext.Clients.All.SendAsync("QueueUpdated");

            _currentVideo = null;

            // Immediately check for next video
            await CheckAndPlayNextVideoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling media ended event");
        }
    }

    private async void OnMediaError(object? sender, string errorMessage)
    {
        try
        {
            if (_currentVideo == null)
            {
                _logger.LogWarning("Media error but no current video tracked");
                return;
            }

            _logger.LogError(
                "Video {VideoId} playback failed: {Error}",
                _currentVideo.Id,
                errorMessage);

            using var scope = _serviceProvider.CreateScope();
            var queueManager = scope.ServiceProvider.GetRequiredService<IQueueManager>();

            // Mark as error
            await queueManager.MarkVideoAsPlayedAsync(
                _currentVideo.Id,
                _currentVideo.Playlist.UserId,
                success: false,
                errorMessage: errorMessage);

            // Broadcast to all clients
            _logger.LogInformation("Broadcasting VideoError event for video {VideoId}", _currentVideo.Id);
            await _hubContext.Clients.All.SendAsync("VideoError", _currentVideo.Id, errorMessage);
            _logger.LogInformation("Broadcasting QueueUpdated event after video error");
            await _hubContext.Clients.All.SendAsync("QueueUpdated");

            _currentVideo = null;

            // Try next video
            await CheckAndPlayNextVideoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling media error event");
        }
    }

    private async Task ResetStuckVideosAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Find any videos stuck in "Playing" status (from previous sessions that crashed)
            var stuckVideos = await context.PlaylistItems
                .Where(i => i.Status == PlaylistItemStatus.Playing)
                .ToListAsync();

            if (stuckVideos.Any())
            {
                _logger.LogInformation("Found {Count} videos stuck in Playing status, resetting to Ready", stuckVideos.Count);

                foreach (var video in stuckVideos)
                {
                    video.Status = PlaylistItemStatus.Ready;
                }

                await context.SaveChangesAsync();

                // Reset queue state
                var queueState = await context.QueueState.FirstOrDefaultAsync();
                if (queueState != null)
                {
                    queueState.CurrentPlaylistItemId = null;
                    queueState.CurrentVideoStartedAt = null;
                    queueState.Status = PlaybackStatus.Idle;
                    queueState.UpdatedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting stuck videos");
        }
    }
}
