using LibVLCSharp.Shared;
using Microsoft.Extensions.Options;
using SingleStepViewer.Configuration;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
    private readonly ILogger<PlaybackService> _logger;
    private readonly PlaybackOptions _playbackOptions;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private bool _isInitialized;
    private bool _disposed;
    private string? _currentFilePath;
    private bool _isRestarting;

    public PlaybackService(ILogger<PlaybackService> logger, IOptions<PlaybackOptions> playbackOptions)
    {
        _logger = logger;
        _playbackOptions = playbackOptions.Value;
    }

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public bool IsPaused => _mediaPlayer?.State == LibVLCSharp.Shared.VLCState.Paused;
    public string? CurrentFilePath => _currentFilePath;
    public float Position => _mediaPlayer?.Position * 100 ?? 0;
    public TimeSpan CurrentTime => TimeSpan.FromMilliseconds(_mediaPlayer?.Time ?? 0);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_mediaPlayer?.Length ?? 0);

    public event EventHandler? MediaEnded;
    public event EventHandler? MediaSkipped;
    public event EventHandler<string>? MediaError;

    public Task InitializeAsync()
    {
        try
        {
            if (_isInitialized)
            {
                _logger.LogWarning("PlaybackService already initialized");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Initializing LibVLC");

            // Initialize LibVLC core
            Core.Initialize();

            // Create LibVLC instance with options
            var options = new List<string>();

            // Basic options
            options.Add("--no-video-title-show"); // Don't show video title on video
            options.Add("--no-osd"); // Disable on-screen display

            // Fullscreen if enabled
            if (_playbackOptions.EnableFullscreen)
            {
                options.Add("--fullscreen");
                options.Add("--video-on-top"); // Keep video on top
            }

            // Disable video output if in development/testing
            if (!_playbackOptions.EnableVideoOutput)
            {
                options.Add("--no-video");
                _logger.LogInformation("Video output disabled (development mode)");
            }

            _libVlc = new LibVLC(options.ToArray());
            _mediaPlayer = new MediaPlayer(_libVlc);

            // Set volume
            _mediaPlayer.Volume = _playbackOptions.DefaultVolume;

            // Subscribe to events
            _mediaPlayer.EndReached += OnMediaEnded;
            _mediaPlayer.EncounteredError += OnMediaError;

            _isInitialized = true;
            _logger.LogInformation("LibVLC initialized successfully");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LibVLC");
            throw;
        }
    }

    public async Task PlayAsync(string filePath)
    {
        try
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            if (_mediaPlayer == null)
            {
                throw new InvalidOperationException("MediaPlayer not initialized");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Video file not found: {filePath}");
            }

            _logger.LogInformation("Playing video: {FilePath}", filePath);

            // Stop any currently playing media
            if (_mediaPlayer.IsPlaying)
            {
                await StopAsync();
            }

            // Create and play media
            using var media = new Media(_libVlc, filePath, FromType.FromPath);
            _mediaPlayer.Play(media);
            _currentFilePath = filePath;

            // Wait a bit to ensure playback starts
            await Task.Delay(100);

            if (_mediaPlayer.IsPlaying)
            {
                _logger.LogInformation("Video playback started successfully");
            }
            else
            {
                _logger.LogWarning("Video playback may not have started");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing video {FilePath}", filePath);
            MediaError?.Invoke(this, ex.Message);
            throw;
        }
    }

    public Task PauseAsync()
    {
        try
        {
            if (_mediaPlayer?.CanPause == true)
            {
                _mediaPlayer.Pause();
                _logger.LogInformation("Playback paused");
            }
            else
            {
                _logger.LogWarning("Cannot pause - no media playing or pause not supported");
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing playback");
            throw;
        }
    }

    public Task ResumeAsync()
    {
        try
        {
            if (_mediaPlayer != null && !_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Play();
                _logger.LogInformation("Playback resumed");
            }
            else
            {
                _logger.LogWarning("Cannot resume - already playing or no media loaded");
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming playback");
            throw;
        }
    }

    public Task SeekAsync(float position)
    {
        try
        {
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
            {
                // Position is 0-100, VLC expects 0.0-1.0
                var vlcPosition = Math.Clamp(position / 100f, 0f, 1f);
                _mediaPlayer.Position = vlcPosition;
                _logger.LogInformation("Seeked to position {Position}%", position.ToString("F1"));
            }
            else
            {
                _logger.LogWarning("Cannot seek - no media loaded");
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeking to position {Position}", position);
            throw;
        }
    }

    public Task StopAsync()
    {
        try
        {
            if (_mediaPlayer?.IsPlaying == true)
            {
                _mediaPlayer.Stop();
                _logger.LogInformation("Playback stopped");
            }
            _currentFilePath = null;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback");
            throw;
        }
    }

    public Task SkipAsync()
    {
        try
        {
            _logger.LogInformation("Skipping current video");

            if (_mediaPlayer?.IsPlaying == true)
            {
                _mediaPlayer.Stop();
            }

            _currentFilePath = null;

            // Skip is explicit; do NOT treat as natural end
            MediaSkipped?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error skipping video");
            throw;
        }
    }

    public async Task RestartAsync()
    {
        try
        {
            if (_mediaPlayer == null || string.IsNullOrEmpty(_currentFilePath))
            {
                _logger.LogWarning("Cannot restart - no media currently loaded");
                return;
            }

            _logger.LogInformation("Restarting current video: {FilePath}", _currentFilePath);

            // Set flag to prevent MediaEnded event from triggering
            _isRestarting = true;

            // Save the file path before stopping
            var filePathToRestart = _currentFilePath;

            // Stop current playback
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }

            // Wait a moment for stop to complete
            await Task.Delay(200);

            // Restart from beginning
            using var media = new Media(_libVlc, filePathToRestart, FromType.FromPath);
            _mediaPlayer.Play(media);
            _currentFilePath = filePathToRestart;

            // Wait a bit to ensure playback starts
            await Task.Delay(200);

            _isRestarting = false;

            if (_mediaPlayer.IsPlaying)
            {
                _logger.LogInformation("Video restarted successfully");
            }
            else
            {
                _logger.LogWarning("Video restart may not have started");
            }
        }
        catch (Exception ex)
        {
            _isRestarting = false;
            _logger.LogError(ex, "Error restarting playback");
            throw;
        }
    }

    public Task SetVolumeAsync(int volume)
    {
        try
        {
            if (_mediaPlayer == null)
            {
                _logger.LogWarning("Cannot set volume - MediaPlayer not initialized");
                return Task.CompletedTask;
            }

            volume = Math.Clamp(volume, 0, 100);
            _mediaPlayer.Volume = volume;
            _logger.LogInformation("Volume set to {Volume}", volume);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting volume");
            throw;
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        // Don't trigger MediaEnded if we're restarting
        if (_isRestarting)
        {
            _logger.LogDebug("Media ended during restart, ignoring event");
            return;
        }

        _logger.LogInformation("Media playback ended");
        MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaError(object? sender, EventArgs e)
    {
        // Don't trigger MediaError if we're restarting
        if (_isRestarting)
        {
            _logger.LogDebug("Media error during restart, ignoring event");
            return;
        }

        var errorMessage = "VLC encountered an error during playback";
        _logger.LogError(errorMessage);
        MediaError?.Invoke(this, errorMessage);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing PlaybackService");

        if (_mediaPlayer != null)
        {
            _mediaPlayer.EndReached -= OnMediaEnded;
            _mediaPlayer.EncounteredError -= OnMediaError;

            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }

            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        _libVlc?.Dispose();
        _libVlc = null;

        _isInitialized = false;
        _disposed = true;
    }
}
