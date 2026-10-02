using System.Globalization;
using System.Net.Sockets;
using IndustrialEquipmentMonitoring.Core.Configuration;
using IndustrialEquipmentMonitoring.Core.Protocol;

namespace IndustrialEquipmentMonitoring.Core.Monitoring;

public sealed class EquipmentService : IAsyncDisposable
{
    private readonly EquipmentConfiguration _configuration;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _logGate = new();
    private EtherNetIpClient? _client;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private int _lastLoggedTemperature = int.MinValue;
    private EquipmentMode? _lastMode;
    private bool _overLimit;
    private int _disposed;

    public EquipmentService(EquipmentConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<EquipmentReading>? ReadingUpdated;

    public event EventHandler<EquipmentEvent>? EventRaised;

    public event EventHandler<bool>? ConnectionChanged;

    public event EventHandler<uint>? SessionChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPollingAsync().ConfigureAwait(false);
            await DisposeClientAsync().ConfigureAwait(false);

            var client = new EtherNetIpClient(_configuration.Address, _configuration.Port);
            try
            {
                await client.RegisterSessionAsync(cancellationToken).ConfigureAwait(false);
                var identity = await client.ListIdentityAsync(cancellationToken).ConfigureAwait(false);

                _client = client;
                IsConnected = true;
                ResetLogState();

                Raise(EventSeverity.Info, $"Connected to {identity.ProductName}");
                Raise(EventSeverity.Info, $"EtherNet/IP session registered (handle 0x{client.SessionHandle:X8})");
                SessionChanged?.Invoke(this, client.SessionHandle);

                if (!string.Equals(identity.ProductName, _configuration.Name, StringComparison.Ordinal))
                {
                    Raise(
                        EventSeverity.Warning,
                        $"Device name '{identity.ProductName}' does not match configuration '{_configuration.Name}'.");
                }

                ConnectionChanged?.Invoke(this, true);

                var pollCts = new CancellationTokenSource();
                _pollCts = pollCts;
                var token = pollCts.Token;

                // The poll loop runs on the thread pool so socket reads never block the UI.
                // Readings are published as events and applied on the WPF dispatcher.
                _pollTask = Task.Run(() => PollLoopAsync(client, token), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _client = null;
                IsConnected = false;
                await client.DisposeAsync().ConfigureAwait(false);

                if (ex is SocketException or IOException or OperationCanceledException)
                {
                    throw new IOException(
                        $"Could not connect to {_configuration.Address}:{_configuration.Port}. Start the equipment simulator, then try again. {ex.Message}",
                        ex);
                }

                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return CommandAsync(EquipmentCommand.Start, "Equipment started", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return CommandAsync(EquipmentCommand.Stop, "Equipment stopped", cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        return CommandAsync(EquipmentCommand.Reset, "Equipment reset", cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var wasConnected = IsConnected || _client is not null;
            await StopPollingAsync().ConfigureAwait(false);
            await DisposeClientAsync().ConfigureAwait(false);

            if (!wasConnected)
            {
                return;
            }

            IsConnected = false;
            SessionChanged?.Invoke(this, 0);
            ConnectionChanged?.Invoke(this, false);
            Raise(EventSeverity.Info, "Disconnected");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private async Task CommandAsync(EquipmentCommand command, string successMessage, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _client;
            if (client is null || !IsConnected)
            {
                throw new InvalidOperationException("Connect to the equipment before sending a command.");
            }

            await client.ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            Raise(EventSeverity.Info, successMessage);

            var reading = await client.ReadEquipmentAsync(cancellationToken).ConfigureAwait(false);
            PublishReading(reading);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task PollLoopAsync(EtherNetIpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var reading = await client.ReadEquipmentAsync(cancellationToken).ConfigureAwait(false);
                PublishReading(reading);
                await Task.Delay(_configuration.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            IsConnected = false;
            SessionChanged?.Invoke(this, 0);
            ConnectionChanged?.Invoke(this, false);
            Raise(EventSeverity.Error, $"Connection lost: {ex.Message}");
        }
    }

    private void PublishReading(EquipmentReading reading)
    {
        ReadingUpdated?.Invoke(this, reading);

        var messages = new List<(EventSeverity Severity, string Message)>();
        lock (_logGate)
        {
            if (_lastMode is EquipmentMode previous
                && previous != reading.Mode
                && reading.Mode == EquipmentMode.Fault)
            {
                messages.Add((EventSeverity.Error, "Equipment faulted"));
            }

            _lastMode = reading.Mode;

            var rounded = (int)Math.Round(reading.TemperatureC, MidpointRounding.AwayFromZero);
            if (rounded != _lastLoggedTemperature)
            {
                _lastLoggedTemperature = rounded;
                messages.Add((EventSeverity.Info, $"Temperature updated: {rounded}°C"));
            }

            var over = reading.TemperatureC > _configuration.TemperatureLimit;
            if (over && !_overLimit)
            {
                _overLimit = true;
                var limit = _configuration.TemperatureLimit.ToString("0", CultureInfo.InvariantCulture);
                messages.Add((EventSeverity.Warning, $"Temperature exceeded threshold ({limit}°C)"));
            }
            else if (!over && _overLimit)
            {
                _overLimit = false;
                messages.Add((EventSeverity.Info, "Temperature returned below the threshold"));
            }
        }

        foreach (var (severity, message) in messages)
        {
            Raise(severity, message);
        }
    }

    private async Task StopPollingAsync()
    {
        var pollCts = _pollCts;
        var pollTask = _pollTask;
        _pollCts = null;
        _pollTask = null;

        if (pollCts is not null)
        {
            await pollCts.CancelAsync().ConfigureAwait(false);
        }

        if (pollTask is not null)
        {
            try
            {
                await pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        pollCts?.Dispose();
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        IsConnected = false;
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ResetLogState()
    {
        lock (_logGate)
        {
            _lastLoggedTemperature = int.MinValue;
            _lastMode = null;
            _overLimit = false;
        }
    }

    private void Raise(EventSeverity severity, string message)
    {
        EventRaised?.Invoke(this, new EquipmentEvent(DateTimeOffset.Now, severity, message));
    }
}
