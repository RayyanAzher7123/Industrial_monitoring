using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using IndustrialEquipmentMonitoring.Core.Monitoring;
using IndustrialEquipmentMonitoring.Core.Protocol;

namespace IndustrialEquipmentMonitoring.Core.Simulation;

public sealed class EtherNetIpDeviceServer
{
    private readonly SimulatedEquipment _equipment;
    private readonly TaskCompletionSource<int> _boundPort = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _nextSession;

    public EtherNetIpDeviceServer(SimulatedEquipment equipment)
    {
        _equipment = equipment;
        _equipment.StateChanged += WriteLog;
    }

    public Task<int> BoundPort => _boundPort.Task;

    public Action<string>? Log { get; set; }

    public async Task RunAsync(IPAddress listenAddress, int port, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(listenAddress, port);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        using var serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clients = new ConcurrentBag<Task>();

        try
        {
            listener.Start();
            if (listener.LocalEndpoint is not IPEndPoint endpoint)
            {
                throw new InvalidOperationException("The simulator could not determine its listen port.");
            }

            _boundPort.TrySetResult(endpoint.Port);
            WriteLog($"Listening on {endpoint.Address}:{endpoint.Port}");
        }
        catch (Exception ex)
        {
            _boundPort.TrySetException(ex);
            throw;
        }

        var tickTask = RunTicksAsync(timer, serverCts.Token);

        try
        {
            while (!serverCts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(serverCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (serverCts.IsCancellationRequested)
                {
                    break;
                }

                clients.Add(HandleClientAsync(client, serverCts.Token));
            }
        }
        finally
        {
            await serverCts.CancelAsync().ConfigureAwait(false);
            listener.Stop();

            try
            {
                await tickTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await Task.WhenAll(clients).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
            }
        }
    }

    private async Task RunTicksAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        var last = DateTimeOffset.UtcNow;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                _equipment.Tick(now - last);
                last = now;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var session = 0u;
            try
            {
                using var stream = client.GetStream();
                var remote = client.Client.RemoteEndPoint;
                WriteLog($"Accepted TCP connection from {remote}");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var header = new byte[EncapsulationPacket.HeaderSize];
                    await StreamIO.ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
                    var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
                if (length > 8192)
                {
                    WriteLog("Closed a connection that sent an oversized payload.");
                    break;
                }

                    var data = new byte[length];
                    if (length > 0)
                    {
                        await StreamIO.ReadExactAsync(stream, data, cancellationToken).ConfigureAwait(false);
                    }

                    var request = EncapsulationPacket.Decode(header, data);
                    var response = Handle(request, ref session, client);
                    if (response is null)
                    {
                        break;
                    }

                    await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                if (session != 0)
                {
                    WriteLog($"Session 0x{session:X8} closed");
                }
            }
        }
    }

    private byte[]? Handle(EncapsulationPacket request, ref uint session, TcpClient client)
    {
        switch (request.Command)
        {
            case EtherNetIpCommands.RegisterSession:
                return Register(request, ref session);
            case EtherNetIpCommands.UnregisterSession:
                if (session == 0 || request.SessionHandle != session)
                {
                    return Error(request, EncapsulationStatus.InvalidSessionHandle);
                }

                WriteLog($"Session 0x{session:X8} unregistered");
                session = 0;
                return null;
            case EtherNetIpCommands.ListIdentity:
                var local = client.Client.LocalEndPoint as IPEndPoint;
                return ListIdentityCodec.BuildResponse(
                    request.SessionHandle,
                    request.SenderContext,
                    _equipment.ProductName,
                    local?.Address ?? IPAddress.Loopback,
                    local?.Port ?? 44818);
            case EtherNetIpCommands.SendRrData:
                if (session == 0 || request.SessionHandle != session)
                {
                    return Error(request, EncapsulationStatus.InvalidSessionHandle);
                }

                return HandleCip(request);
            default:
                return Error(request, EncapsulationStatus.InvalidCommand);
        }
    }

    private byte[] Register(EncapsulationPacket request, ref uint session)
    {
        if (request.Data.Length < 4)
        {
            return Error(request, EncapsulationStatus.IncorrectData);
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(request.Data);
        if (version != 1)
        {
            return Error(request, EncapsulationStatus.UnsupportedProtocolVersion);
        }

        session = (uint)Interlocked.Increment(ref _nextSession);
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
        WriteLog($"Session 0x{session:X8} registered");

        return new EncapsulationPacket(
            request.Command,
            session,
            EncapsulationStatus.Success,
            request.SenderContext,
            data).Encode();
    }

    private byte[] HandleCip(EncapsulationPacket request)
    {
        byte[] cip;
        try
        {
            cip = SendRrDataCodec.UnwrapCip(request.Data);
        }
        catch (InvalidDataException)
        {
            return Error(request, EncapsulationStatus.IncorrectData);
        }

        if (!CipMessage.TryReadPath(
                cip,
                out var service,
                out var classId,
                out var instanceId,
                out var attributeId,
                out var hasAttribute,
                out var serviceData))
        {
            var rejected = cip.Length > 0 ? cip[0] : (byte)0;
            return CipReply(request, CipMessage.Failure(rejected, CipStatus.PathDestinationUnknown));
        }

        byte[] response = (classId, instanceId) switch
        {
            (CipObjects.IdentityClass, CipObjects.IdentityInstance) =>
                HandleIdentity(service, hasAttribute, attributeId),
            (CipObjects.EquipmentClass, CipObjects.EquipmentInstance) =>
                HandleEquipment(service, hasAttribute, attributeId, serviceData),
            _ => CipMessage.Failure(service, CipStatus.PathDestinationUnknown)
        };

        return CipReply(request, response);
    }

    private byte[] HandleIdentity(byte service, bool hasAttribute, byte attributeId)
    {
        if (service == CipServices.GetAttributeSingle
            && hasAttribute
            && attributeId == CipObjects.ProductNameAttribute)
        {
            var name = System.Text.Encoding.ASCII.GetBytes(_equipment.ProductName);
            var data = new byte[1 + name.Length];
            data[0] = (byte)name.Length;
            name.CopyTo(data.AsSpan(1));
            return CipMessage.Success(service, data);
        }

        return CipMessage.Failure(service, CipStatus.ServiceNotSupported);
    }

    private byte[] HandleEquipment(byte service, bool hasAttribute, byte attributeId, ReadOnlySpan<byte> data)
    {
        switch (service)
        {
            case CipServices.GetAttributesAll:
            {
                var reading = _equipment.Read();
                WriteLog(
                    $"CIP Get_Attributes_All -> {reading.Mode} {reading.TemperatureC:0.0}°C {reading.SpeedRpm} RPM {reading.PressurePsi} PSI");

                return CipMessage.Success(service, EquipmentDataCodec.Encode(reading));
            }
            case CipServices.SetAttributeSingle
                when hasAttribute && attributeId == CipObjects.CommandAttribute:
            {
                if (data.Length != 1 || data[0] is not (1 or 2 or 3))
                {
                    return CipMessage.Failure(service, CipStatus.InvalidParameter);
                }

                var status = _equipment.TryCommand((EquipmentCommand)data[0]);
                return status == CipStatus.Success
                    ? CipMessage.Success(service, [])
                    : CipMessage.Failure(service, status);
            }
            default:
                return CipMessage.Failure(service, CipStatus.ServiceNotSupported);
        }
    }

    private static byte[] CipReply(EncapsulationPacket request, byte[] cip)
    {
        return SendRrDataCodec.Wrap(request.SessionHandle, cip, request.SenderContext);
    }

    private void WriteLog(string message)
    {
        var log = Log;
        if (log is null)
        {
            return;
        }

        // Console output must not stall the CIP response. A selected console
        // window on Windows blocks WriteLine until the selection is cleared.
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (callback, text) = ((Action<string>, string))state!;
            try
            {
                callback(text);
            }
            catch (IOException)
            {
            }
        }, (log, message));
    }

    private static byte[] Error(EncapsulationPacket request, uint status)
    {
        return new EncapsulationPacket(
            request.Command,
            request.SessionHandle,
            status,
            request.SenderContext,
            []).Encode();
    }
}
