using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

public sealed class ListIdentityResult
{
    public ListIdentityResult(string productName, ushort vendorId, int port)
    {
        ProductName = productName;
        VendorId = vendorId;
        Port = port;
    }

    public string ProductName { get; }

    public ushort VendorId { get; }

    public int Port { get; }
}

public static class ListIdentityCodec
{
    public const ushort CipIdentityItem = 0x000C;

    public static byte[] BuildResponse(
        uint sessionHandle,
        byte[] senderContext,
        string productName,
        IPAddress address,
        int port)
    {
        var nameBytes = Encoding.ASCII.GetBytes(productName);
        if (nameBytes.Length > 31)
        {
            nameBytes = nameBytes[..31];
        }

        var itemDataLength = 34 + nameBytes.Length;
        var payload = new byte[6 + itemDataLength];
        var span = payload.AsSpan();
        var offset = 0;

        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], 1);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], CipIdentityItem);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)itemDataLength);
        offset += 2;

        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], 1);
        offset += 2;

        // The socket address inside ListIdentity is network byte order.
        BinaryPrimitives.WriteInt16BigEndian(span[offset..], 2);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], (ushort)port);
        offset += 2;

        var ipBytes = address.GetAddressBytes();
        if (ipBytes.Length != 4)
        {
            ipBytes = IPAddress.Loopback.GetAddressBytes();
        }

        ipBytes.CopyTo(span[offset..]);
        offset += 4;
        offset += 8;

        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], SimulatedIdentity.VendorId);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], SimulatedIdentity.DeviceType);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], SimulatedIdentity.ProductCode);
        offset += 2;
        span[offset++] = SimulatedIdentity.RevisionMajor;
        span[offset++] = SimulatedIdentity.RevisionMinor;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], 0);
        offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], SimulatedIdentity.SerialNumber);
        offset += 4;
        span[offset++] = (byte)nameBytes.Length;
        nameBytes.CopyTo(span[offset..]);
        offset += nameBytes.Length;
        span[offset] = 0x03;

        return new EncapsulationPacket(
            EtherNetIpCommands.ListIdentity,
            sessionHandle,
            EncapsulationStatus.Success,
            senderContext,
            payload).Encode();
    }

    public static ListIdentityResult Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
        {
            throw new InvalidDataException("ListIdentity response is too short.");
        }

        var itemCount = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        if (itemCount < 1)
        {
            throw new InvalidDataException("ListIdentity response contained no identity items.");
        }

        var type = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        if (type != CipIdentityItem)
        {
            throw new InvalidDataException($"Unexpected ListIdentity item type 0x{type:X4}.");
        }

        if (payload.Length < 6 + length || length < 33)
        {
            throw new InvalidDataException("ListIdentity item is truncated.");
        }

        var item = payload.Slice(6, length);
        var port = BinaryPrimitives.ReadUInt16BigEndian(item[4..]);
        var vendorId = BinaryPrimitives.ReadUInt16LittleEndian(item[18..]);
        var nameLength = item[32];
        if (item.Length < 33 + nameLength)
        {
            throw new InvalidDataException("ListIdentity product name is truncated.");
        }

        var productName = Encoding.ASCII.GetString(item.Slice(33, nameLength));
        return new ListIdentityResult(productName, vendorId, port);
    }
}
