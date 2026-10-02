using System.Buffers.Binary;
using System.Net.Sockets;
using IndustrialEquipmentMonitoring.Core.Monitoring;

namespace IndustrialEquipmentMonitoring.Core.Protocol;

public sealed class EtherNetIpClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private uint _context;
    private int _disposed;

    public EtherNetIpClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public uint SessionHandle { get; private set; }

    public async Task RegisterSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _tcp = new TcpClient();
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            await _tcp.ConnectAsync(_host, _port, connectTimeout.Token).ConfigureAwait(false);
            _stream = _tcp.GetStream();

            var requestData = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(requestData, 1);
            var request = new EncapsulationPacket(
                EtherNetIpCommands.RegisterSession,
                0,
                EncapsulationStatus.Success,
                NextContext(),
                requestData).Encode();

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            var response = await TransactAsync(request, responseTimeout.Token).ConfigureAwait(false);

            EnsureSuccess(response);
            if (response.SessionHandle == 0)
            {
                throw new IOException("The device did not assign an EtherNet/IP session.");
            }

            SessionHandle = response.SessionHandle;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            CloseSocket();
            throw new IOException("Timed out waiting for the device to register a session.");
        }
        catch
        {
            CloseSocket();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ListIdentityResult> ListIdentityAsync(CancellationToken cancellationToken)
    {
        var request = new EncapsulationPacket(
            EtherNetIpCommands.ListIdentity,
            SessionHandle,
            EncapsulationStatus.Success,
            NextContext(),
            []).Encode();

        var response = await SendLockedAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        return ListIdentityCodec.Parse(response.Data);
    }

    public async Task<EquipmentReading> ReadEquipmentAsync(CancellationToken cancellationToken)
    {
        var cip = CipMessage.GetAttributesAll(CipObjects.EquipmentClass, CipObjects.EquipmentInstance);
        var response = await SendCipAsync(cip, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new IOException($"CIP read failed with status 0x{response.Status:X2}.");
        }

        return EquipmentDataCodec.Decode(response.Data);
    }

    public async Task ExecuteCommandAsync(EquipmentCommand command, CancellationToken cancellationToken)
    {
        var cip = CipMessage.SetAttributeSingle(
            CipObjects.EquipmentClass,
            CipObjects.EquipmentInstance,
            CipObjects.CommandAttribute,
            (byte)command);

        var response = await SendCipAsync(cip, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
        {
            return;
        }

        throw new EquipmentCommandException(response.Status, DescribeFailure(response.Status, command));
    }

    public async Task UnregisterSessionAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stream is not null && SessionHandle != 0)
            {
                var request = new EncapsulationPacket(
                    EtherNetIpCommands.UnregisterSession,
                    SessionHandle,
                    EncapsulationStatus.Success,
                    NextContext(),
                    []).Encode();

                try
                {
                    await _stream.WriteAsync(request).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
                {
                }
            }
        }
        finally
        {
            CloseSocket();
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await UnregisterSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async Task<CipResponse> SendCipAsync(byte[] cip, CancellationToken cancellationToken)
    {
        if (SessionHandle == 0 || _stream is null)
        {
            throw new InvalidOperationException("Register an EtherNet/IP session before sending CIP messages.");
        }

        var packet = SendRrDataCodec.Wrap(SessionHandle, cip, NextContext());
        var response = await SendLockedAsync(packet, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        if (response.Command != EtherNetIpCommands.SendRrData)
        {
            throw new IOException($"Unexpected encapsulation command 0x{response.Command:X4}.");
        }

        return CipMessage.Parse(SendRrDataCodec.UnwrapCip(response.Data));
    }

    private async Task<EncapsulationPacket> SendLockedAsync(byte[] packet, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                return await TransactAsync(packet, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                CloseSocket();
                throw new IOException("Timed out waiting for the device.");
            }
            catch
            {
                CloseSocket();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EncapsulationPacket> TransactAsync(byte[] packet, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);

        var header = new byte[EncapsulationPacket.HeaderSize];
        await StreamIO.ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        if (length > 8192)
        {
            throw new IOException("The device sent an encapsulation payload that is too large.");
        }

        var data = new byte[length];
        if (length > 0)
        {
            await StreamIO.ReadExactAsync(stream, data, cancellationToken).ConfigureAwait(false);
        }

        return EncapsulationPacket.Decode(header, data);
    }

    private byte[] NextContext()
    {
        var context = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(context, ++_context);
        return context;
    }

    private static void EnsureSuccess(EncapsulationPacket packet)
    {
        if (packet.Status == EncapsulationStatus.Success)
        {
            return;
        }

        var reason = packet.Status switch
        {
            EncapsulationStatus.InvalidCommand => "invalid command",
            EncapsulationStatus.IncorrectData => "incorrect data",
            EncapsulationStatus.InvalidSessionHandle => "invalid session handle",
            EncapsulationStatus.UnsupportedProtocolVersion => "unsupported protocol version",
            _ => $"status 0x{packet.Status:X8}"
        };

        throw new IOException($"EtherNet/IP encapsulation failed: {reason}.");
    }

    private static string DescribeFailure(byte status, EquipmentCommand command)
    {
        return status switch
        {
            CipStatus.DeviceStateConflict when command == EquipmentCommand.Start =>
                "Start was rejected because the equipment is faulted. Reset it first.",
            CipStatus.DeviceStateConflict =>
                $"{command} was rejected because the equipment state does not allow it.",
            CipStatus.InvalidParameter => "The device rejected the command parameter.",
            CipStatus.ServiceNotSupported => "The device does not support that CIP service.",
            CipStatus.PathDestinationUnknown => "The CIP object path was not found on the device.",
            _ => $"CIP command failed with status 0x{status:X2}."
        };
    }

    private void CloseSocket()
    {
        var stream = _stream;
        var tcp = _tcp;
        _stream = null;
        _tcp = null;
        SessionHandle = 0;

        try
        {
            stream?.Dispose();
        }
        catch (IOException)
        {
        }

        try
        {
            tcp?.Dispose();
        }
        catch (IOException)
        {
        }
    }
}
