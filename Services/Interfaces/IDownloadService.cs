namespace SingleStepViewer.Services.Interfaces;

public interface IDownloadService
{
    Task<DownloadResult> DownloadVideoAsync(string videoUrl, string outputPath);
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? ErrorMessage { get; set; }
}
