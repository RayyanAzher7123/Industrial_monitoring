using System.Buffers.Binary;
using IndustrialEquipmentMonitoring.Core.Monitoring;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

/// <summary>
/// Fixed CIP payload for the vendor-specific equipment object:
/// mode (USINT), temperature °C (REAL), speed RPM (UINT), pressure PSI (UINT), status (USINT).
/// </summary>
public static class EquipmentDataCodec
{
    public const int PayloadLength = 10;

    public static byte[] Encode(EquipmentReading reading)
    {
        var buffer = new byte[PayloadLength];
        buffer[0] = (byte)reading.Mode;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), (float)reading.TemperatureC);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(5), ToUInt16(reading.SpeedRpm));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(7), ToUInt16(reading.PressurePsi));
        buffer[9] = reading.DeviceOverTemperature ? (byte)1 : (byte)0;
        return buffer;
    }

    public static EquipmentReading Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < PayloadLength)
        {
            throw new InvalidDataException("Equipment data payload is too short.");
        }

        var mode = data[0] switch
        {
            (byte)EquipmentMode.Stopped => EquipmentMode.Stopped,
            (byte)EquipmentMode.Running => EquipmentMode.Running,
            _ => EquipmentMode.Fault
        };

        return new EquipmentReading(
            mode,
            BinaryPrimitives.ReadSingleLittleEndian(data[1..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[7..]),
            data[9] != 0);
    }

    private static ushort ToUInt16(int value)
    {
        return (ushort)Math.Clamp(value, 0, ushort.MaxValue);
    }
}
