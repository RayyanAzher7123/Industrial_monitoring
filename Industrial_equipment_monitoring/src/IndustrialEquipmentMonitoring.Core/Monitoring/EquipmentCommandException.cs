namespace IndustrialEquipmentMonitoring.Core.Monitoring;

public sealed class EquipmentCommandException : Exception
{
    public EquipmentCommandException(byte cipStatus, string message)
        : base(message)
    {
        CipStatus = cipStatus;
    }

    public byte CipStatus { get; }
}
