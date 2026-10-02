using System.Buffers.Binary;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

public static class SendRrDataCodec
{
    public const ushort NullAddressItem = 0x0000;
    public const ushort UnconnectedDataItem = 0x00B2;

    public static byte[] Wrap(uint sessionHandle, ReadOnlySpan<byte> cip, byte[] senderContext)
    {
        var payload = new byte[16 + cip.Length];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 5);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], NullAddressItem);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], UnconnectedDataItem);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)cip.Length);
        cip.CopyTo(span[16..]);

        return new EncapsulationPacket(
            EtherNetIpCommands.SendRrData,
            sessionHandle,
            EncapsulationStatus.Success,
            senderContext,
            payload).Encode();
    }

    public static byte[] UnwrapCip(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            throw new InvalidDataException("SendRRData payload is too short.");
        }

        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        var offset = 8;
        byte[]? cip = null;

        for (var item = 0; item < itemCount; item++)
        {
            if (payload.Length < offset + 4)
            {
                throw new InvalidDataException("SendRRData item is truncated.");
            }

            var type = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..]);
            offset += 4;

            if (payload.Length < offset + length)
            {
                throw new InvalidDataException("SendRRData item data is truncated.");
            }

            if (type == UnconnectedDataItem)
            {
                cip = payload.Slice(offset, length).ToArray();
            }

            offset += length;
        }

        return cip ?? throw new InvalidDataException("SendRRData did not contain a CIP data item.");
    }
}
