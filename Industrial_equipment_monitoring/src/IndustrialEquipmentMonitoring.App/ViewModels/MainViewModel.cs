using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using IndustrialEquipmentMonitoring.Core.Configuration;
using IndustrialEquipmentMonitoring.Core.Monitoring;

namespace IndustrialEquipmentMonitoring.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly EquipmentService? _service;
    private readonly double _temperatureLimit;
    private readonly MetricItem _connection;
    private readonly MetricItem _modeMetric;
    private readonly MetricItem _temperature;
    private readonly MetricItem _speed;
    private readonly MetricItem _pressure;
    private EquipmentMode? _mode;
    private string _sessionText = "No session";
    private bool _isConnected;
    private bool _isConnecting;
    private bool _shutdown;

    public MainViewModel()
    {
        _connection = new MetricItem("CONNECTION", "Disconnected", "No session", "Idle");
        _modeMetric = new MetricItem("MODE", "—", "Waiting", "Idle");
        _temperature = new MetricItem("TEMPERATURE", "—", "Limit —", "Idle");
        _speed = new MetricItem("SPEED", "—", "RPM", "Idle");
        _pressure = new MetricItem("PRESSURE", "—", "PSI", "Idle");
        Metrics = new ObservableCollection<MetricItem>
        {
            _connection,
            _modeMetric,
            _temperature,
            _speed,
            _pressure
        };

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect, LogUnexpected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected, LogUnexpected);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart, LogUnexpected);
        StopCommand = new AsyncRelayCommand(StopAsync, CanStop, LogUnexpected);
        ResetCommand = new AsyncRelayCommand(ResetAsync, CanReset, LogUnexpected);

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "equipment.xml");
            var configuration = EquipmentConfigurationLoader.Load(path);
            _temperatureLimit = configuration.TemperatureLimit;
            EquipmentName = configuration.Name;
            EndpointText = $"{configuration.Address}:{configuration.Port}";
            _temperature.Caption = $"Limit {configuration.TemperatureLimit.ToString("0", CultureInfo.CurrentCulture)}°C";

            _service = new EquipmentService(configuration);
            _service.ReadingUpdated += OnReading;
            _service.EventRaised += OnEquipmentEvent;
            _service.ConnectionChanged += OnConnectionChanged;
            _service.SessionChanged += OnSessionChanged;

            AddLog(
                EventSeverity.Info,
                $"Configuration loaded for {configuration.Name} at {EndpointText}.");
            AddLog(
                EventSeverity.Info,
                $"Temperature limit is {configuration.TemperatureLimit.ToString("0", CultureInfo.CurrentCulture)}°C. Start the simulator, then press Connect.");
        }
        catch (Exception ex)
        {
            EquipmentName = "Configuration error";
            EndpointText = "equipment.xml";
            AddLog(EventSeverity.Error, ex.Message);
        }
    }

    public ObservableCollection<MetricItem> Metrics { get; }

    public ObservableCollection<LogEntry> Events { get; } = new();

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand ResetCommand { get; }

    public string EquipmentName { get; private set; } = "Equipment";

    public string EndpointText { get; private set; } = "";

    public string SessionText
    {
        get => _sessionText;
        private set => SetProperty(ref _sessionText, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public async Task ShutdownAsync()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        if (_service is not null)
        {
            await _service.DisposeAsync();
        }
    }

    private bool CanConnect()
    {
        return _service is not null && !IsConnected && !_isConnecting;
    }

    private bool CanStart()
    {
        return IsConnected && _mode is null or EquipmentMode.Stopped;
    }

    private bool CanStop()
    {
        return IsConnected && _mode == EquipmentMode.Running;
    }

    private bool CanReset()
    {
        return IsConnected && _mode is EquipmentMode.Running or EquipmentMode.Fault;
    }

    private async Task ConnectAsync()
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            _isConnecting = true;
            _connection.Value = "Connecting";
            _connection.Tone = "Warn";
            _connection.Caption = EndpointText;
            NotifyCommands();
            await _service.ConnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _connection.Value = "Disconnected";
            _connection.Tone = "Idle";
            _connection.Caption = "No session";
            AddLog(EventSeverity.Error, ex.Message);
        }
        finally
        {
            _isConnecting = false;
            NotifyCommands();
        }
    }

    private async Task DisconnectAsync()
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            await _service.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AddLog(EventSeverity.Error, ex.Message);
        }
    }

    private Task StartAsync()
    {
        return SendCommand(service => service.StartAsync(CancellationToken.None));
    }

    private Task StopAsync()
    {
        return SendCommand(service => service.StopAsync(CancellationToken.None));
    }

    private Task ResetAsync()
    {
        return SendCommand(service => service.ResetAsync(CancellationToken.None));
    }

    private async Task SendCommand(Func<EquipmentService, Task> command)
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            await command(_service);
        }
        catch (Exception ex)
        {
            AddLog(EventSeverity.Error, ex.Message);
        }
    }

    private void OnReading(object? sender, EquipmentReading reading)
    {
        RunOnUi(() =>
        {
            _mode = reading.Mode;
            var overTemperature = reading.TemperatureC > _temperatureLimit;

            _modeMetric.Value = reading.Mode.ToString();
            _modeMetric.Tone = reading.Mode switch
            {
                EquipmentMode.Running => "Good",
                EquipmentMode.Fault => "Fault",
                _ => "Idle"
            };
            _modeMetric.Caption = reading.Mode switch
            {
                EquipmentMode.Running => "Cycle in progress",
                EquipmentMode.Fault => "Reset required",
                _ => "Ready"
            };

            _temperature.Value = FormatTemperature(reading.TemperatureC);
            _temperature.Tone = reading.Mode == EquipmentMode.Fault
                ? "Fault"
                : overTemperature ? "Warn" : "Idle";

            _speed.Value = reading.SpeedRpm.ToString("N0", CultureInfo.CurrentCulture) + " RPM";
            _pressure.Value = reading.PressurePsi.ToString("N0", CultureInfo.CurrentCulture) + " PSI";
            NotifyCommands();
        });
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        RunOnUi(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                _connection.Value = "Connected";
                _connection.Tone = "Good";
                _connection.Caption = SessionText;
            }
            else
            {
                _connection.Value = "Disconnected";
                _connection.Tone = "Idle";
                _connection.Caption = "No session";
                ClearTelemetry();
            }

            NotifyCommands();
        });
    }

    private void OnSessionChanged(object? sender, uint session)
    {
        RunOnUi(() =>
        {
            SessionText = session == 0 ? "No session" : $"Session 0x{session:X8}";
            if (IsConnected)
            {
                _connection.Caption = SessionText;
            }
        });
    }

    private void OnEquipmentEvent(object? sender, EquipmentEvent equipmentEvent)
    {
        RunOnUi(() => AddLog(equipmentEvent.Severity, equipmentEvent.Message, equipmentEvent.Timestamp));
    }

    private void ClearTelemetry()
    {
        _mode = null;
        _modeMetric.Value = "—";
        _modeMetric.Caption = "Waiting";
        _modeMetric.Tone = "Idle";
        _temperature.Value = "—";
        _temperature.Tone = "Idle";
        _speed.Value = "—";
        _pressure.Value = "—";
    }

    private void AddLog(EventSeverity severity, string message, DateTimeOffset? timestamp = null)
    {
        var time = (timestamp ?? DateTimeOffset.Now).ToLocalTime();
        Events.Add(new LogEntry
        {
            TimeText = time.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            Severity = severity,
            Message = message
        });

        while (Events.Count > 200)
        {
            Events.RemoveAt(0);
        }
    }

    private void LogUnexpected(Exception exception)
    {
        RunOnUi(() => AddLog(EventSeverity.Error, exception.Message));
    }

    private void NotifyCommands()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
    }

    private static string FormatTemperature(double value)
    {
        var rounded = Math.Round(value, 1);
        var whole = Math.Abs(rounded - Math.Round(rounded)) < 0.001;
        var text = whole
            ? Math.Round(rounded).ToString("0", CultureInfo.CurrentCulture)
            : rounded.ToString("0.0", CultureInfo.CurrentCulture);
        return text + "°C";
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
