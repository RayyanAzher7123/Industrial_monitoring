using IndustrialEquipmentMonitoring.Core.Monitoring;
using IndustrialEquipmentMonitoring.Core.Protocol;

namespace IndustrialEquipmentMonitoring.Core.Simulation;

public sealed class SimulatedEquipment
{
    public const double AmbientTemperatureC = 25;
    public const double TargetSpeedRpm = 1250;
    public const double TargetPressurePsi = 42;
    public const double FaultMarginC = 15;

    private readonly object _gate = new();
    private readonly string _productName;
    private readonly double _temperatureLimit;
    private EquipmentMode _mode = EquipmentMode.Stopped;
    private double _temperatureC = AmbientTemperatureC;
    private double _speedRpm;
    private double _pressurePsi;

    public SimulatedEquipment(string productName, double temperatureLimit)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new ArgumentException("Equipment name is required.", nameof(productName));
        }

        if (temperatureLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureLimit));
        }

        _productName = productName.Trim();
        _temperatureLimit = temperatureLimit;
    }

    public string ProductName
    {
        get
        {
            lock (_gate)
            {
                return _productName;
            }
        }
    }

    public double FaultTemperatureC => _temperatureLimit + FaultMarginC;

    public event Action<string>? StateChanged;

    public void Tick(TimeSpan elapsed)
    {
        string? notice = null;
        var seconds = Math.Clamp(elapsed.TotalSeconds, 0, 1);

        lock (_gate)
        {
            if (_mode == EquipmentMode.Running)
            {
                _speedRpm = Approach(_speedRpm, TargetSpeedRpm, 500 * seconds);
                _pressurePsi = Approach(_pressurePsi, TargetPressurePsi, 18 * seconds);

                var heatRate = _temperatureC < _temperatureLimit ? 5.0 : 1.2;
                _temperatureC += heatRate * seconds;

                if (_temperatureC >= FaultTemperatureC)
                {
                    _mode = EquipmentMode.Fault;
                    notice = $"Fault: temperature reached {FaultTemperatureC:0}°C. Reset is required before start.";
                }
                else
                {
                    _speedRpm = Math.Clamp(_speedRpm + (Random.Shared.NextDouble() * 16 - 8), 0, 4000);
                    _pressurePsi = Math.Clamp(_pressurePsi + (Random.Shared.NextDouble() * 1.2 - 0.6), 0, 200);
                }
            }
            else
            {
                _speedRpm = Approach(_speedRpm, 0, 700 * seconds);
                _pressurePsi = Approach(_pressurePsi, 0, 25 * seconds);
                _temperatureC = Approach(_temperatureC, AmbientTemperatureC, 1.5 * seconds);
            }
        }

        if (notice is not null)
        {
            StateChanged?.Invoke(notice);
        }
    }

    public EquipmentReading Read()
    {
        lock (_gate)
        {
            return new EquipmentReading(
                _mode,
                Math.Round(_temperatureC, 1),
                (int)Math.Round(_speedRpm),
                (int)Math.Round(_pressurePsi),
                _temperatureC > _temperatureLimit);
        }
    }

    public byte TryCommand(EquipmentCommand command)
    {
        string? notice = null;
        byte status;

        lock (_gate)
        {
            switch (command)
            {
                case EquipmentCommand.Start when _mode == EquipmentMode.Fault:
                    status = CipStatus.DeviceStateConflict;
                    break;
                case EquipmentCommand.Start:
                    _mode = EquipmentMode.Running;
                    status = CipStatus.Success;
                    notice = "Command: Start";
                    break;
                case EquipmentCommand.Stop when _mode == EquipmentMode.Running:
                    _mode = EquipmentMode.Stopped;
                    status = CipStatus.Success;
                    notice = "Command: Stop";
                    break;
                case EquipmentCommand.Stop:
                    status = CipStatus.Success;
                    break;
                case EquipmentCommand.Reset:
                    _mode = EquipmentMode.Stopped;
                    status = CipStatus.Success;
                    notice = "Command: Reset";
                    break;
                default:
                    status = CipStatus.InvalidParameter;
                    break;
            }
        }

        if (notice is not null)
        {
            StateChanged?.Invoke(notice);
        }

        return status;
    }

    private static double Approach(double current, double target, double maxStep)
    {
        var delta = target - current;
        if (Math.Abs(delta) <= maxStep)
        {
            return target;
        }

        return current + (Math.Sign(delta) * maxStep);
    }
}
