namespace SingleStepViewer.Configuration;

public class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    public double PriorityWeight { get; set; } = 10.0;
    public double FairnessWeight { get; set; } = 5.0;
    public int UserCooldownMinutes { get; set; } = 15;
    public int QueueCheckIntervalSeconds { get; set; } = 5;
}
