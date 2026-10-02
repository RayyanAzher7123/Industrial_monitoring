namespace IndustrialEquipmentMonitoring.Core.Configuration;

public sealed class EquipmentConfiguration
{
    public required string Name { get; init; }

    public required string Address { get; init; }

    public required int Port { get; init; }

    public required double TemperatureLimit { get; init; }

    public required int PollIntervalMs { get; init; }
}
