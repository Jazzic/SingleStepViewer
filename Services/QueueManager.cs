using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SingleStepViewer.Configuration;
using SingleStepViewer.Data;
using SingleStepViewer.Data.Entities;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class QueueManager : IQueueManager
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<QueueManager> _logger;
    private readonly SchedulingOptions _schedulingOptions;

    public QueueManager(
        ApplicationDbContext context,
        ILogger<QueueManager> logger,
        IOptions<SchedulingOptions> schedulingOptions)
    {
        _context = context;
        _logger = logger;
        _schedulingOptions = schedulingOptions.Value;
    }

    public async Task<PlaylistItem?> GetNextVideoAsync()
    {
        try
        {
            // Get all ready videos with their user information
            var readyVideos = await _context.PlaylistItems
                .Include(i => i.Playlist)
                .ThenInclude(p => p.User)
                .Where(i => i.Status == PlaylistItemStatus.Ready)
                .ToListAsync();

            if (!readyVideos.Any())
            {
                _logger.LogDebug("No videos in queue");
                return null;
            }

            // Get queue state to track fairness
            var queueState = await GetOrCreateQueueStateAsync();
            var lastUserPlayed = queueState.GetLastUserPlayed();

            // Calculate scores for each video
            var scoredVideos = readyVideos.Select(video =>
            {
                var score = CalculateScore(video, lastUserPlayed);
                return new { Video = video, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

            if (scoredVideos.Any())
            {
                var selected = scoredVideos.First();
                _logger.LogInformation(
                    "Selected video {VideoId} ({Title}) from user {UserId} with score {Score}",
                    selected.Video.Id,
                    selected.Video.Title,
                    selected.Video.Playlist.UserId,
                    selected.Score);

                return selected.Video;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next video from queue");
            return null;
        }
    }

    public async Task<IEnumerable<PlaylistItem>> GetUpcomingVideosAsync(int count = 10)
    {
        try
        {
            // Get the current playing video ID to exclude it from the queue
            var queueState = await GetOrCreateQueueStateAsync();
            var currentVideoId = queueState.CurrentPlaylistItemId;

            // Get all videos that could be in the queue (Ready, Downloading, Pending)
            // Exclude Playing status and the current video
            var queueableVideos = await _context.PlaylistItems
                .Include(i => i.Playlist)
                .ThenInclude(p => p.User)
                .Where(i => (i.Status == PlaylistItemStatus.Ready ||
                           i.Status == PlaylistItemStatus.Downloading ||
                           i.Status == PlaylistItemStatus.Pending) &&
                           i.Id != currentVideoId)
                .ToListAsync();

            if (!queueableVideos.Any())
            {
                return Enumerable.Empty<PlaylistItem>();
            }

            var lastUserPlayed = queueState.GetLastUserPlayed();

            // Calculate scores and sort (only Ready videos get scored, others are shown at the end)
            var readyVideos = queueableVideos.Where(v => v.Status == PlaylistItemStatus.Ready).ToList();
            var nonReadyVideos = queueableVideos.Where(v => v.Status != PlaylistItemStatus.Ready).ToList();

            var scoredVideos = readyVideos.Select(video =>
            {
                var score = CalculateScore(video, lastUserPlayed);
                return new { Video = video, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .Select(x => x.Video)
            .ToList();

            // Add non-ready videos at the end
            scoredVideos.AddRange(nonReadyVideos.Take(Math.Max(0, count - scoredVideos.Count)));

            return scoredVideos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting upcoming videos");
            return Enumerable.Empty<PlaylistItem>();
        }
    }

    public async Task MarkVideoAsPlayedAsync(int playlistItemId, string userId, bool success = true, string? errorMessage = null)
    {
        try
        {
            var item = await _context.PlaylistItems
                .Include(i => i.Playlist)
                .FirstOrDefaultAsync(i => i.Id == playlistItemId);

            if (item == null)
            {
                _logger.LogWarning("PlaylistItem {PlaylistItemId} not found", playlistItemId);
                return;
            }

            // Update item status based on playlist settings
            if (success)
            {
                // If the playlist has RemoveAfterPlaying=false, reset to Ready instead of marking as Played
                if (!item.Playlist.RemoveAfterPlaying)
                {
                    item.Status = PlaylistItemStatus.Ready;
                    _logger.LogInformation(
                        "Video {PlaylistItemId} reset to Ready (playlist has RemoveAfterPlaying=false)",
                        playlistItemId);
                }
                else
                {
                    item.Status = PlaylistItemStatus.Played;
                }
            }
            else
            {
                item.Status = PlaylistItemStatus.Error;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    item.ErrorMessage = errorMessage;
                }
            }

            // Create playback history record
            var history = new PlaybackHistory
            {
                PlaylistItemId = playlistItemId,
                UserId = userId,
                PlayedAt = DateTime.UtcNow,
                CompletedSuccessfully = success,
                ErrorMessage = errorMessage
            };

            _context.PlaybackHistory.Add(history);

            // Update user's last played time
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.LastPlayedAt = DateTime.UtcNow;
            }

            // Update queue state fairness tracking
            var queueState = await GetOrCreateQueueStateAsync();
            queueState.UpdateUserPlayedTime(userId, DateTime.UtcNow);
            queueState.CurrentPlaylistItemId = null;
            queueState.CurrentVideoStartedAt = null;
            queueState.Status = PlaybackStatus.Idle;
            queueState.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Marked video {PlaylistItemId} as {Status} for user {UserId}",
                playlistItemId,
                success ? (item.Playlist.RemoveAfterPlaying ? "played" : "ready (for replay)") : "error",
                userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking video {PlaylistItemId} as played", playlistItemId);
        }
    }

    private double CalculateScore(PlaylistItem video, Dictionary<string, DateTime> lastUserPlayed)
    {
        // Get user ID from the playlist
        var userId = video.Playlist.UserId;

        // Base score from priority (1-10)
        var priorityScore = video.Priority * _schedulingOptions.PriorityWeight;

        // Fairness boost based on time since user's last video
        var fairnessBoost = 0.0;
        if (lastUserPlayed.TryGetValue(userId, out var lastPlayed))
        {
            var minutesSinceLastPlayed = (DateTime.UtcNow - lastPlayed).TotalMinutes;

            // Apply fairness weight: longer time = higher boost
            // Divided by 10 to normalize minutes, then multiply by fairness weight
            fairnessBoost = (minutesSinceLastPlayed / 10.0) * _schedulingOptions.FairnessWeight;
        }
        else
        {
            // User has never had a video played - give maximum fairness boost
            // Use UserCooldownMinutes as the baseline for "maximum wait time"
            fairnessBoost = (_schedulingOptions.UserCooldownMinutes / 10.0) * _schedulingOptions.FairnessWeight;
        }

        // Final score
        var totalScore = priorityScore + fairnessBoost;

        _logger.LogDebug(
            "Score for video {VideoId} ({Title}) from user {UserId}: Priority={Priority} ({PriorityScore}), Fairness={FairnessBoost}, Total={TotalScore}",
            video.Id,
            video.Title,
            userId,
            video.Priority,
            priorityScore,
            fairnessBoost,
            totalScore);

        return totalScore;
    }

    private async Task<QueueState> GetOrCreateQueueStateAsync()
    {
        var queueState = await _context.QueueState.FirstOrDefaultAsync();
        if (queueState == null)
        {
            queueState = new QueueState { Id = 1 };
            _context.QueueState.Add(queueState);
            await _context.SaveChangesAsync();
        }
        return queueState;
    }
}
