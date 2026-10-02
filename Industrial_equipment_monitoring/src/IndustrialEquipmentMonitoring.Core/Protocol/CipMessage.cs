using System.Buffers.Binary;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

public static class CipMessage
{
    public static byte[] GetAttributesAll(byte classId, byte instanceId)
    {
        return
        [
            CipServices.GetAttributesAll,
            0x02,
            0x20, classId,
            0x24, instanceId
        ];
    }

    public static byte[] GetAttributeSingle(byte classId, byte instanceId, byte attributeId)
    {
        return
        [
            CipServices.GetAttributeSingle,
            0x03,
            0x20, classId,
            0x24, instanceId,
            0x30, attributeId
        ];
    }

    public static byte[] SetAttributeSingle(byte classId, byte instanceId, byte attributeId, byte value)
    {
        return
        [
            CipServices.SetAttributeSingle,
            0x03,
            0x20, classId,
            0x24, instanceId,
            0x30, attributeId,
            value
        ];
    }

    public static byte[] Success(byte service, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[4 + data.Length];
        buffer[0] = (byte)(service | CipServices.ReplyFlag);
        buffer[2] = CipStatus.Success;
        data.CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    public static byte[] Failure(byte service, byte status)
    {
        return
        [
            (byte)(service | CipServices.ReplyFlag),
            0,
            status,
            0
        ];
    }

    public static CipResponse Parse(ReadOnlySpan<byte> message)
    {
        if (message.Length < 4)
        {
            throw new InvalidDataException("CIP response is too short.");
        }

        var dataOffset = 4 + (message[3] * 2);
        if (message.Length < dataOffset)
        {
            throw new InvalidDataException("CIP response is truncated.");
        }

        return new CipResponse(message[0], message[2], message[dataOffset..].ToArray());
    }

    public static bool TryReadPath(
        ReadOnlySpan<byte> cip,
        out byte service,
        out byte classId,
        out byte instanceId,
        out byte attributeId,
        out bool hasAttribute,
        out ReadOnlySpan<byte> serviceData)
    {
        service = 0;
        classId = 0;
        instanceId = 0;
        attributeId = 0;
        hasAttribute = false;
        serviceData = default;

        if (cip.Length < 2)
        {
            return false;
        }

        service = cip[0];
        var pathLength = cip[1] * 2;
        if (cip.Length < 2 + pathLength)
        {
            return false;
        }

        var path = cip.Slice(2, pathLength);
        var sawClass = false;
        var sawInstance = false;
        var index = 0;
        while (index < path.Length)
        {
            var segment = path[index++];
            if (index >= path.Length)
            {
                return false;
            }

            switch (segment)
            {
                case 0x20:
                    classId = path[index++];
                    sawClass = true;
                    break;
                case 0x24:
                    instanceId = path[index++];
                    sawInstance = true;
                    break;
                case 0x30:
                    attributeId = path[index++];
                    hasAttribute = true;
                    break;
                default:
                    return false;
            }
        }

        if (!sawClass || !sawInstance)
        {
            return false;
        }

        serviceData = cip[(2 + pathLength)..];
        return true;
    }
}

public readonly record struct CipResponse(byte Service, byte Status, byte[] Data)
{
    public bool IsSuccess => Status == CipStatus.Success;
}
