namespace IndustrialEquipmentMonitoring.Core.Monitoring;

public enum EquipmentMode : byte
{
    Stopped = 0,
    Running = 1,
    Fault = 2
}

public enum EquipmentCommand : byte
{
    Start = 1,
    Stop = 2,
    Reset = 3
}

public enum EventSeverity
{
    Info,
    Warning,
    Error
}

public sealed class EquipmentEvent
{
    public EquipmentEvent(DateTimeOffset timestamp, EventSeverity severity, string message)
    {
        Timestamp = timestamp;
        Severity = severity;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public EventSeverity Severity { get; }

    public string Message { get; }
}

public sealed class EquipmentReading
{
    public EquipmentReading(
        EquipmentMode mode,
        double temperatureC,
        int speedRpm,
        int pressurePsi,
        bool deviceOverTemperature)
    {
        Mode = mode;
        TemperatureC = temperatureC;
        SpeedRpm = speedRpm;
        PressurePsi = pressurePsi;
        DeviceOverTemperature = deviceOverTemperature;
    }

    public EquipmentMode Mode { get; }

    public double TemperatureC { get; }

    public int SpeedRpm { get; }

    public int PressurePsi { get; }

    /// <summary>
    /// Status bit reported by the device. The operator alarm limit still comes from XML.
    /// </summary>
    public bool DeviceOverTemperature { get; }
}
