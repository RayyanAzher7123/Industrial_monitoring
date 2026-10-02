using System.Net;
using System.Net.Sockets;
using IndustrialEquipmentMonitoring.Core.Configuration;
using IndustrialEquipmentMonitoring.Core.Simulation;

namespace IndustrialEquipmentMonitoring.Simulator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--self-check"))
        {
            return await SelfCheck.RunAsync();
        }

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "equipment.xml");
            var config = EquipmentConfigurationLoader.Load(configPath);
            var equipment = new SimulatedEquipment(config.Name, config.TemperatureLimit);
            var server = new EtherNetIpDeviceServer(equipment)
            {
                Log = message => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}")
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            var listenAddress = IPAddress.TryParse(config.Address, out var parsed) && IPAddress.IsLoopback(parsed)
                ? IPAddress.Loopback
                : IPAddress.Any;

            Console.WriteLine(config.Name);
            Console.WriteLine($"EtherNet/IP explicit messaging for {config.Address}:{config.Port}");
            Console.WriteLine($"Temperature warning at {config.TemperatureLimit:0}°C, device fault at {equipment.FaultTemperatureC:0}°C");
            Console.WriteLine("Press Ctrl+C to stop.");
            Console.WriteLine();

            await server.RunAsync(listenAddress, config.Port, cts.Token);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
