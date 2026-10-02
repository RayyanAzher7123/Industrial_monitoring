namespace IndustrialEquipmentMonitoring.Core.Protocol;

/// <summary>
/// Explicit EtherNet/IP messaging on TCP port 44818.
/// Encapsulation fields are little-endian. CIP is carried inside SendRRData.
/// Implicit I/O on UDP port 2222 is not part of this simulator.
/// </summary>
public static class EtherNetIpCommands
{
    public const ushort ListIdentity = 0x0063;
    public const ushort RegisterSession = 0x0065;
    public const ushort UnregisterSession = 0x0066;
    public const ushort SendRrData = 0x006F;
}

public static class EncapsulationStatus
{
    public const uint Success = 0x0000;
    public const uint InvalidCommand = 0x0001;
    public const uint IncorrectData = 0x0003;
    public const uint InvalidSessionHandle = 0x0064;
    public const uint UnsupportedProtocolVersion = 0x0069;
}

public static class CipServices
{
    public const byte GetAttributesAll = 0x01;
    public const byte GetAttributeSingle = 0x0E;
    public const byte SetAttributeSingle = 0x10;
    public const byte ReplyFlag = 0x80;
}

public static class CipStatus
{
    public const byte Success = 0x00;
    public const byte PathDestinationUnknown = 0x05;
    public const byte ServiceNotSupported = 0x08;
    public const byte DeviceStateConflict = 0x10;
    public const byte InvalidParameter = 0x20;
}

public static class CipObjects
{
    public const byte IdentityClass = 0x01;
    public const byte IdentityInstance = 0x01;
    public const byte ProductNameAttribute = 0x07;

    /// <summary>Vendor-specific object implemented by this simulator.</summary>
    public const byte EquipmentClass = 0x64;
    public const byte EquipmentInstance = 0x01;
    public const byte CommandAttribute = 0x06;
}

/// <summary>
/// Identity values for the simulator. They are not ODVA-assigned identifiers.
/// </summary>
public static class SimulatedIdentity
{
    public const ushort VendorId = 0x9999;
    public const ushort DeviceType = 0x0064;
    public const ushort ProductCode = 0x0001;
    public const byte RevisionMajor = 1;
    public const byte RevisionMinor = 0;
    public const uint SerialNumber = 1;
}
