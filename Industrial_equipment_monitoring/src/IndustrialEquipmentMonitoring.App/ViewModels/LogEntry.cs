using IndustrialEquipmentMonitoring.Core.Monitoring;

namespace IndustrialEquipmentMonitoring.App.ViewModels;

public sealed class LogEntry
{
    public required string TimeText { get; init; }

    public required EventSeverity Severity { get; init; }

    public required string Message { get; init; }

    public string SeverityText => Severity switch
    {
        EventSeverity.Warning => "WARN",
        EventSeverity.Error => "ERROR",
        _ => "INFO"
    };
}
