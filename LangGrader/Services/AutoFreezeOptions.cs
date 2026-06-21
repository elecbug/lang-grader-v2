namespace LangGrader.Services;

public sealed class AutoFreezeOptions
{
    public bool Enabled { get; set; } = true;

    public int StartupDelaySeconds { get; set; } = 10;

    public int CheckIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 20;
}