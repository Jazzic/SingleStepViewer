namespace SingleStepViewer.Configuration;

public class VideoOptions
{
    public const string SectionName = "Video";

    public string StoragePath { get; set; } = "./videos";
    public int MaxConcurrentDownloads { get; set; } = 2;
    public string YtDlpPath { get; set; } = "yt-dlp.exe";
    public string PreferredFormat { get; set; } = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";
    public string? CookiesFilePath { get; set; }
    public string? AdditionalArguments { get; set; }
}
