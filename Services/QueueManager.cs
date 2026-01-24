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

    // ==========================
    // Scheduling/scoring settings
    // ==========================
    //
    // Video cooldown window (Tv): recently played videos get penalized.
    private const double TvHours = 24.0;

    // User fairness window (Tu): users who haven't had anything played get a boost, capped at Tu.
    private const double TuHours = 24.0;

    // Scales:
    // - videoPenaltyScale: points per hour of "recently played" penalty
    // - userBoostScale: points per hour of "user has waited" boost
    //
    // Example intuition with these defaults:
    // - A video played "just now" gets a penalty of (TvHours * videoPenaltyScale) => 24 * 0.5 = 12 points.
    // - A user who hasn't had anything played for 24h gets a boost of (TuHours * userBoostScale) => 24 * 0.25 = 6 points.
    private const double VideoPenaltyScale = 0.5;
    private const double UserBoostScale = 0.25;

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
            // Get queue state (we use it to exclude the currently playing item if needed later)
            var queueState = await GetOrCreateQueueStateAsync();

            // Load all Ready videos with user info (needed for user boost)
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

            // Build a lookup: PlaylistItemId -> last time this video was played (UTC)
            // If missing => "never played"
            var readyVideoIds = readyVideos.Select(v => v.Id).ToList();

            var lastPlayedByVideoId = await _context.PlaybackHistory
                .Where(h => readyVideoIds.Contains(h.PlaylistItemId))
                .GroupBy(h => h.PlaylistItemId)
                .Select(g => new { PlaylistItemId = g.Key, LastPlayedAt = g.Max(x => x.PlayedAt) })
                .ToDictionaryAsync(x => x.PlaylistItemId, x => x.LastPlayedAt);

            // Score and pick best
            var scoredVideos = readyVideos
                .Select(video => new
                {
                    Video = video,
                    Score = CalculateScore(
                        video,
                        lastPlayedByVideoId.TryGetValue(video.Id, out var lp) ? lp : null)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            var selected = scoredVideos.FirstOrDefault();
            if (selected == null)
            {
                return null;
            }

            _logger.LogInformation(
                "Selected video {VideoId} ({Title}) from user {UserId} with score {Score}",
                selected.Video.Id,
                selected.Video.Title,
                selected.Video.Playlist.UserId,
                selected.Score);

            return selected.Video;
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
            var queueState = await GetOrCreateQueueStateAsync();
            var currentVideoId = queueState.CurrentPlaylistItemId;

            // Same as before: show Ready first (scored), then non-ready at the end.
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

            var readyVideos = queueableVideos.Where(v => v.Status == PlaylistItemStatus.Ready).ToList();
            var nonReadyVideos = queueableVideos.Where(v => v.Status != PlaylistItemStatus.Ready).ToList();

            // Lookup last played times for Ready items (to compute lpVideoHours)
            var readyIds = readyVideos.Select(v => v.Id).ToList();
            var lastPlayedByVideoId = await _context.PlaybackHistory
                .Where(h => readyIds.Contains(h.PlaylistItemId))
                .GroupBy(h => h.PlaylistItemId)
                .Select(g => new { PlaylistItemId = g.Key, LastPlayedAt = g.Max(x => x.PlayedAt) })
                .ToDictionaryAsync(x => x.PlaylistItemId, x => x.LastPlayedAt);

            var scoredReady = readyVideos
                .Select(video => new
                {
                    Video = video,
                    Score = CalculateScore(
                        video,
                        lastPlayedByVideoId.TryGetValue(video.Id, out var lp) ? lp : null)
                })
                .OrderByDescending(x => x.Score)
                .Take(count)
                .Select(x => x.Video)
                .ToList();

            scoredReady.AddRange(nonReadyVideos.Take(Math.Max(0, count - scoredReady.Count)));
            return scoredReady;
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

            // History record (used for lpVideoHours)
            _context.PlaybackHistory.Add(new PlaybackHistory
            {
                PlaylistItemId = playlistItemId,
                UserId = userId,
                PlayedAt = DateTime.UtcNow,
                CompletedSuccessfully = success,
                ErrorMessage = errorMessage
            });

            // User last played time (used for lpuHours)
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user != null)
            {
                user.LastPlayedAt = DateTime.UtcNow;
            }

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

    /// <summary>
    /// Calculates the final score for a single video.
    /// Higher score means it should be played sooner.
    ///
    /// Formula:
    ///   videoPenalty = clamp(Tv - lpVideoHours, 0, Tv)
    ///   userBoost    = clamp(lpuHours, 0, Tu)
    ///   score        = up - videoPenaltyScale * videoPenalty + userBoostScale * userBoost
    ///
    /// Where:
    /// - up = PlaylistItem.Priority (1..10)
    /// - lpVideoHours = hours since *this video* last played (null => never played => no penalty)
    /// - lpuHours = hours since *this user* last played anything (null => never played => max boost)
    /// </summary>
    private double CalculateScore(PlaylistItem video, DateTime? lastPlayedAtUtc)
    {
        var now = DateTime.UtcNow;

        // up: user priority (1..10)
        var up = video.Priority;

        // lpVideoHours: null means "never played" => treat as "infinite hours ago" => penalty becomes 0
        var lpVideoHours = lastPlayedAtUtc.HasValue
            ? (now - lastPlayedAtUtc.Value).TotalHours
            : double.PositiveInfinity;

        // lpuHours: null means "user never played anything" => treat as "infinite hours ago" => max boost
        var lastUserPlayedAtUtc = video.Playlist.User.LastPlayedAt;
        var lpuHours = lastUserPlayedAtUtc.HasValue
            ? (now - lastUserPlayedAtUtc.Value).TotalHours
            : double.PositiveInfinity;

        var videoPenaltyHours = Clamp(TvHours - lpVideoHours, min: 0.0, max: TvHours);
        var userBoostHours = Clamp(lpuHours, min: 0.0, max: TuHours);

        var score = up
                    - (VideoPenaltyScale * videoPenaltyHours)
                    + (UserBoostScale * userBoostHours);

        _logger.LogDebug(
            "Score video {VideoId}: up={Up}, lpVideoHours={LpVideoHours:0.00}, videoPenaltyHours={VideoPenaltyHours:0.00}, lpuHours={LpuHours:0.00}, userBoostHours={UserBoostHours:0.00}, score={Score:0.00}",
            video.Id,
            up,
            double.IsInfinity(lpVideoHours) ? -1 : lpVideoHours,
            videoPenaltyHours,
            double.IsInfinity(lpuHours) ? -1 : lpuHours,
            userBoostHours,
            score);

        return score;
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));

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
