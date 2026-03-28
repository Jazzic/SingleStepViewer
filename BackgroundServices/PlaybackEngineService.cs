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

    private readonly object _mediaEndedLock = new();
    private int? _mediaEndedHandledForVideoId;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);

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
            _playbackService.MediaSkipped += OnMediaSkipped;
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
            _playbackService.MediaSkipped -= OnMediaSkipped;
            _playbackService.MediaError -= OnMediaError;
            _logger.LogInformation("Playback Engine Service stopped");
        }
    }

    private async Task CheckAndPlayNextVideoAsync()
    {
        _logger.LogDebug("CheckAndPlayNextVideoAsync starting - IsPlaying={IsPlaying}, CurrentVideo={CurrentVideoId}",
            _playbackService.IsPlaying, _currentVideo?.Id);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var queueManager = scope.ServiceProvider.GetRequiredService<IQueueManager>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Get next video from queue
            _logger.LogDebug("Calling GetNextVideoAsync");
            var nextVideo = await queueManager.GetNextVideoAsync();

            if (nextVideo == null)
            {
                _logger.LogDebug("No videos in queue");
                return;
            }

            _logger.LogDebug("Got next video from queue: {VideoId} - {Title}", nextVideo.Id, nextVideo.Title);

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

            // New video => reset de-dupe latch
            lock (_mediaEndedLock)
            {
                _mediaEndedHandledForVideoId = null;
            }

            _logger.LogDebug("Calling PlayAsync for video {VideoId}: {FilePath}", nextVideo.Id, nextVideo.LocalFilePath);
            await _playbackService.PlayAsync(nextVideo.LocalFilePath);
            _logger.LogDebug("PlayAsync completed for video {VideoId}, IsPlaying={IsPlaying}",
                nextVideo.Id, _playbackService.IsPlaying);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking and playing next video");
        }
    }

    private async void OnMediaSkipped(object? sender, EventArgs e)
    {
        await HandleTransitionAsync(success: true, errorMessage: null, reason: "skipped");
    }

    private async void OnMediaEnded(object? sender, EventArgs e)
    {
        _logger.LogDebug("OnMediaEnded event received from PlaybackService");
        await HandleTransitionAsync(success: true, errorMessage: null, reason: "ended");
    }

    private async Task HandleTransitionAsync(bool success, string? errorMessage, string reason)
    {
        _logger.LogDebug("HandleTransitionAsync called with reason={Reason}, success={Success}", reason, success);

        if (!await _transitionLock.WaitAsync(0))
        {
            _logger.LogDebug("Ignoring {Reason} transition because another transition is in progress", reason);
            return;
        }

        _logger.LogDebug("Acquired transition lock for {Reason}", reason);

        try
        {
            if (_currentVideo == null)
            {
                _logger.LogWarning("{Reason} but no current video tracked", reason);
                return;
            }

            // Check for duplicate media ended events
            lock (_mediaEndedLock)
            {
                if (_mediaEndedHandledForVideoId == _currentVideo.Id)
                {
                    _logger.LogDebug("Ignoring duplicate {Reason} event for video {VideoId}", reason, _currentVideo.Id);
                    return;
                }
                _mediaEndedHandledForVideoId = _currentVideo.Id;
            }

            var finished = _currentVideo;

            _logger.LogInformation("Finalizing current video {VideoId} ({Reason})", finished.Id, reason);

            using var scope = _serviceProvider.CreateScope();
            var queueManager = scope.ServiceProvider.GetRequiredService<IQueueManager>();

            _logger.LogDebug("Calling MarkVideoAsPlayedAsync for video {VideoId}", finished.Id);
            await queueManager.MarkVideoAsPlayedAsync(
                finished.Id,
                finished.Playlist.UserId,
                success: success,
                errorMessage: errorMessage);

            _logger.LogDebug("Broadcasting VideoEnded and QueueUpdated events");
            await _hubContext.Clients.All.SendAsync("VideoEnded", finished.Id);
            await _hubContext.Clients.All.SendAsync("QueueUpdated");

            _currentVideo = null;

            // Give VLC a moment to fully clean up after EndReached before starting next video
            _logger.LogDebug("Waiting 300ms for VLC to clean up before starting next video");
            await Task.Delay(300);

            _logger.LogDebug("Calling CheckAndPlayNextVideoAsync from HandleTransitionAsync");
            await CheckAndPlayNextVideoAsync();
            _logger.LogDebug("CheckAndPlayNextVideoAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {Reason} transition", reason);
        }
        finally
        {
            _logger.LogDebug("Releasing transition lock for {Reason}", reason);
            _transitionLock.Release();
        }
    }

    private async void OnMediaError(object? sender, string errorMessage)
    {
        await HandleTransitionAsync(success: false, errorMessage: errorMessage, reason: "error");
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
