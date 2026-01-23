namespace SingleStepViewer.Services.Interfaces;

public interface IVideoService
{
    Task<VideoMetadata?> ExtractMetadataAsync(string videoUrl);
}

public class VideoMetadata
{
    public string Title { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public TimeSpan? Duration { get; set; }
}
