namespace SingleStepViewer.Configuration;

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public string VlcPath { get; set; } = "C:\\Program Files\\VideoLAN\\VLC";
    public int DefaultVolume { get; set; } = 80;
    public bool EnableFullscreen { get; set; } = true;
    public bool EnableVideoOutput { get; set; } = true;
}
