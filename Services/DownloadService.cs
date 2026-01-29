using System.Diagnostics;
using Microsoft.Extensions.Options;
using SingleStepViewer.Configuration;
using SingleStepViewer.Services.Interfaces;

namespace SingleStepViewer.Services;

public class DownloadService : IDownloadService
{
    private readonly ILogger<DownloadService> _logger;
    private readonly VideoOptions _videoOptions;

    public DownloadService(ILogger<DownloadService> logger, IOptions<VideoOptions> videoOptions)
    {
        _logger = logger;
        _videoOptions = videoOptions.Value;
    }

    public async Task<DownloadResult> DownloadVideoAsync(string videoUrl, string outputPath)
    {
        try
        {
            _logger.LogInformation("Starting download for {VideoUrl} to {OutputPath}", videoUrl, outputPath);

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Set output template (without extension, yt-dlp adds it)
            var outputTemplate = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? "",
                Path.GetFileNameWithoutExtension(outputPath)
            );

            // Build arguments using ArgumentList to prevent command injection
            var startInfo = new ProcessStartInfo
            {
                FileName = _videoOptions.YtDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Add arguments safely using ArgumentList
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add($"{outputTemplate}.%(ext)s");
            startInfo.ArgumentList.Add("--no-playlist");

            // Enable Android client for YouTube extraction (avoids PO Token requirements)
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=android,web");

            // Add cookies if configured
            if (!string.IsNullOrEmpty(_videoOptions.CookiesFilePath) && File.Exists(_videoOptions.CookiesFilePath))
            {
                startInfo.ArgumentList.Add("--cookies");
                startInfo.ArgumentList.Add(_videoOptions.CookiesFilePath);
            }

            // Format preference - prefer pre-combined formats (no merging needed)
            if (!string.IsNullOrEmpty(_videoOptions.PreferredFormat))
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add(_videoOptions.PreferredFormat);
            }
            else
            {
                // Default to best quality single file (already has video+audio combined)
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("best[ext=mp4]/best");
            }

            // Add any additional user-specified arguments
            // Note: Arguments with spaces should be added as separate entries in config
            // Example in appsettings.json: "AdditionalArguments": "--merge-output-format mp4 --no-check-certificate"
            if (!string.IsNullOrEmpty(_videoOptions.AdditionalArguments))
            {
                // Simple space-based splitting - for complex arguments, configure them separately
                var additionalArgs = _videoOptions.AdditionalArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var arg in additionalArgs)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            // Add video URL (this is user-controlled, so using ArgumentList prevents injection)
            startInfo.ArgumentList.Add(videoUrl);

            using var process = new Process { StartInfo = startInfo };

            // Log output as it comes
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogDebug("yt-dlp output: {Output}", e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogDebug("yt-dlp: {Message}", e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var errorMessage = $"yt-dlp exited with code {process.ExitCode}";
                _logger.LogError("Download failed for {VideoUrl}: {Error}", videoUrl, errorMessage);

                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                };
            }

            // Find the downloaded file (yt-dlp may have added extension)
            var directory = Path.GetDirectoryName(outputPath) ?? "";
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(outputPath);
            var downloadedFiles = Directory.GetFiles(directory, fileNameWithoutExt + ".*")
                .Where(f => !f.EndsWith(".part") && !f.EndsWith(".ytdl"))
                .ToList();

            if (downloadedFiles.Count == 0)
            {
                _logger.LogError("Download completed but file not found: {OutputPath}", outputPath);
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = "Download completed but file not found"
                };
            }

            var actualFilePath = downloadedFiles.First();
            _logger.LogInformation("Successfully downloaded {VideoUrl} to {FilePath}", videoUrl, actualFilePath);

            return new DownloadResult
            {
                Success = true,
                FilePath = actualFilePath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading video {VideoUrl}", videoUrl);
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
