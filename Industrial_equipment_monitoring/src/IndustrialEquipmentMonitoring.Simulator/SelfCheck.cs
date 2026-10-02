using System.Net;
using System.Net.Sockets;
using IndustrialEquipmentMonitoring.Core.Configuration;
using IndustrialEquipmentMonitoring.Core.Monitoring;
using IndustrialEquipmentMonitoring.Core.Protocol;
using IndustrialEquipmentMonitoring.Core.Simulation;

namespace IndustrialEquipmentMonitoring.Simulator;

public static class SelfCheck
{
    public static async Task<int> RunAsync()
    {
        try
        {
            await RunCoreAsync();
            Console.WriteLine("Self-check passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Self-check failed:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunCoreAsync()
    {
        CheckPhysics();
        await CheckServicePollAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var serverCts = new CancellationTokenSource();
        var equipment = new SimulatedEquipment("Welding Unit 01", 80);
        var server = new EtherNetIpDeviceServer(equipment);
        var serverTask = server.RunAsync(IPAddress.Loopback, 0, serverCts.Token);
        EtherNetIpClient? client = null;

        try
        {
            var port = await server.BoundPort.WaitAsync(timeout.Token);
            client = new EtherNetIpClient(IPAddress.Loopback.ToString(), port);
            await client.RegisterSessionAsync(timeout.Token);
            Check(client.SessionHandle != 0, "Session handle was not assigned.");

            var identity = await client.ListIdentityAsync(timeout.Token);
            Check(identity.ProductName == "Welding Unit 01", "ListIdentity product name did not match.");
            Check(identity.VendorId == SimulatedIdentity.VendorId, "ListIdentity vendor id did not match.");

            var stopped = await client.ReadEquipmentAsync(timeout.Token);
            Check(stopped.Mode == EquipmentMode.Stopped, "Equipment should start stopped.");

            await client.ExecuteCommandAsync(EquipmentCommand.Start, timeout.Token);
            var started = await client.ReadEquipmentAsync(timeout.Token);
            Check(started.Mode == EquipmentMode.Running, "Start did not change the mode to Running.");

            await Task.Delay(700, timeout.Token);
            var moving = await client.ReadEquipmentAsync(timeout.Token);
            Check(moving.SpeedRpm > 50, $"Speed did not rise after start. Speed was {moving.SpeedRpm}.");
            Check(moving.PressurePsi > 0, "Pressure did not rise after start.");

            await client.ExecuteCommandAsync(EquipmentCommand.Stop, timeout.Token);
            var halted = await client.ReadEquipmentAsync(timeout.Token);
            Check(halted.Mode == EquipmentMode.Stopped, "Stop did not change the mode to Stopped.");

            await client.ExecuteCommandAsync(EquipmentCommand.Start, timeout.Token);
            for (var tick = 0; tick < 40; tick++)
            {
                equipment.Tick(TimeSpan.FromSeconds(1));
            }

            var faulted = await client.ReadEquipmentAsync(timeout.Token);
            Check(faulted.Mode == EquipmentMode.Fault, "Equipment did not fault after sustained heating.");

            var rejected = false;
            try
            {
                await client.ExecuteCommandAsync(EquipmentCommand.Start, timeout.Token);
            }
            catch (EquipmentCommandException ex) when (ex.CipStatus == CipStatus.DeviceStateConflict)
            {
                rejected = true;
            }

            Check(rejected, "Start was accepted while the equipment was faulted.");

            await client.ExecuteCommandAsync(EquipmentCommand.Reset, timeout.Token);
            var reset = await client.ReadEquipmentAsync(timeout.Token);
            Check(reset.Mode == EquipmentMode.Stopped, "Reset did not return the equipment to Stopped.");

            await serverCts.CancelAsync();
            await serverTask;

            var lost = false;
            try
            {
                await client.ReadEquipmentAsync(timeout.Token);
            }
            catch (IOException)
            {
                lost = true;
            }

            Check(lost, "A read should fail after the simulator stops.");
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            await serverCts.CancelAsync();
            try
            {
                await serverTask;
            }
            catch
            {
            }
        }

        await CheckConnectionRefusedAsync();
    }

    private static async Task CheckServicePollAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var serverCts = new CancellationTokenSource();
        var equipment = new SimulatedEquipment("Welding Unit 01", 80);
        var server = new EtherNetIpDeviceServer(equipment);
        var serverTask = server.RunAsync(IPAddress.Loopback, 0, serverCts.Token);
        var readings = 0;
        EquipmentService? service = null;

        try
        {
            var port = await server.BoundPort.WaitAsync(timeout.Token);
            var configuration = new EquipmentConfiguration
            {
                Name = "Welding Unit 01",
                Address = IPAddress.Loopback.ToString(),
                Port = port,
                TemperatureLimit = 80,
                PollIntervalMs = 200
            };

            service = new EquipmentService(configuration);
            service.ReadingUpdated += (_, _) => Interlocked.Increment(ref readings);
            await service.ConnectAsync(timeout.Token);
            await Task.Delay(1000, timeout.Token);
            Check(readings >= 3, $"Background poll produced {readings} readings; expected at least 3.");
            await service.StartAsync(timeout.Token);
            Check(equipment.Read().Mode == EquipmentMode.Running, "Service start did not run the equipment.");
            await service.DisconnectAsync();
        }
        finally
        {
            if (service is not null)
            {
                await service.DisposeAsync();
            }

            await serverCts.CancelAsync();
            try
            {
                await serverTask;
            }
            catch
            {
            }
        }
    }

    private static void CheckPhysics()
    {
        var equipment = new SimulatedEquipment("Welding Unit 01", 80);
        var idle = equipment.Read();
        Check(idle.Mode == EquipmentMode.Stopped, "A new simulator should be stopped.");
        Check(idle.SpeedRpm == 0, "A stopped simulator should report zero speed.");

        Check(equipment.TryCommand(EquipmentCommand.Start) == CipStatus.Success, "Start should succeed from Stopped.");
        for (var tick = 0; tick < 40 && equipment.Read().Mode != EquipmentMode.Fault; tick++)
        {
            equipment.Tick(TimeSpan.FromSeconds(1));
        }

        var faulted = equipment.Read();
        Check(faulted.Mode == EquipmentMode.Fault, "Direct simulation did not reach Fault.");
        Check(faulted.DeviceOverTemperature, "Faulted equipment should report the over-temperature bit.");
        Check(equipment.TryCommand(EquipmentCommand.Start) == CipStatus.DeviceStateConflict, "Start should conflict while faulted.");
        Check(equipment.TryCommand(EquipmentCommand.Reset) == CipStatus.Success, "Reset should succeed.");
        Check(equipment.Read().Mode == EquipmentMode.Stopped, "Reset should leave the equipment stopped.");
    }

    private static async Task CheckConnectionRefusedAsync()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var client = new EtherNetIpClient(IPAddress.Loopback.ToString(), port);
        var refused = false;
        try
        {
            await client.RegisterSessionAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            refused = true;
        }
        finally
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
            }
        }

        Check(refused, "Connecting to a closed port should fail.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
