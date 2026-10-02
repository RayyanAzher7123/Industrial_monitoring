using System.Buffers.Binary;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

public sealed class EncapsulationPacket
{
    public const int HeaderSize = 24;

    public EncapsulationPacket(ushort command, uint sessionHandle, uint status, byte[] senderContext, byte[] data)
    {
        Command = command;
        SessionHandle = sessionHandle;
        Status = status;
        SenderContext = new byte[8];
        senderContext.AsSpan(0, Math.Min(8, senderContext.Length)).CopyTo(SenderContext);
        Data = data;
    }

    public ushort Command { get; }

    public uint SessionHandle { get; }

    public uint Status { get; }

    public byte[] SenderContext { get; }

    public byte[] Data { get; }

    public byte[] Encode()
    {
        if (Data.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("Encapsulation payload is too large.");
        }

        var buffer = new byte[HeaderSize + Data.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, Command);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)Data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], SessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], Status);
        SenderContext.CopyTo(span[12..]);
        Data.CopyTo(span[HeaderSize..]);
        return buffer;
    }

    public static EncapsulationPacket Decode(ReadOnlySpan<byte> header, ReadOnlySpan<byte> data)
    {
        if (header.Length < HeaderSize)
        {
            throw new InvalidDataException("Encapsulation header is incomplete.");
        }

        return new EncapsulationPacket(
            BinaryPrimitives.ReadUInt16LittleEndian(header),
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            header.Slice(12, 8).ToArray(),
            data.ToArray());
    }
}
