namespace SingleStepViewer.Services.Interfaces;

public interface IPlaybackService
{
    Task InitializeAsync();
    Task PlayAsync(string filePath);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task RestartAsync();
    Task SkipAsync();
    Task SetVolumeAsync(int volume);
    bool IsPlaying { get; }
    bool IsPaused { get; }
    string? CurrentFilePath { get; }
    event EventHandler? MediaEnded;
    event EventHandler? MediaSkipped;
    event EventHandler<string>? MediaError;
}
