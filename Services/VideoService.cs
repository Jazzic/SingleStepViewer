using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SingleStepViewer.Configuration;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class VideoService : IVideoService
{
    private readonly ILogger<VideoService> _logger;
    private readonly VideoOptions _videoOptions;

    public VideoService(ILogger<VideoService> logger, IOptions<VideoOptions> videoOptions)
    {
        _logger = logger;
        _videoOptions = videoOptions.Value;
    }

    public async Task<VideoMetadata?> ExtractMetadataAsync(string videoUrl)
    {
        try
        {
            _logger.LogInformation("Extracting metadata for {VideoUrl}", videoUrl);

            var startInfo = new ProcessStartInfo
            {
                FileName = _videoOptions.YtDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Use ArgumentList to prevent command injection
            startInfo.ArgumentList.Add("--dump-json");
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add(videoUrl);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("Failed to extract metadata for {VideoUrl}: {Error}", videoUrl, error);
                return null;
            }

            // Parse JSON output
            var jsonDoc = JsonDocument.Parse(output);
            var root = jsonDoc.RootElement;

            var metadata = new VideoMetadata
            {
                Title = root.TryGetProperty("title", out var title) ? title.GetString() ?? "Unknown Title" : "Unknown Title",
                ThumbnailUrl = root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null,
                Duration = root.TryGetProperty("duration", out var dur) && dur.TryGetDouble(out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : null
            };

            _logger.LogInformation("Extracted metadata for {VideoUrl}: {Title}, Duration: {Duration}",
                videoUrl, metadata.Title, metadata.Duration);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata for {VideoUrl}", videoUrl);
            return null;
        }
    }
}
