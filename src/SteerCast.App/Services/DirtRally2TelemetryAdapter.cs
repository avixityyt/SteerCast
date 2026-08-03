using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// Receives DiRT Rally 2.0's opt-in localhost UDP telemetry and derives a
/// display-only load signal from G-force and suspension motion. This is not FFB.
/// </summary>
public sealed class DirtRally2TelemetryAdapter : IGameTelemetrySource
{
    public const int DefaultPort = 20777;
    public const string GameId = "dirt-rally-2";
    private const int PacketLength = 66 * sizeof(float);
    private const double ActiveThreshold = 0.05;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(750);
    private readonly CancellationTokenSource _stopping = new();
    private readonly UdpClient? _client;
    private readonly Task? _listener;
    private readonly int _port;
    private GameTelemetryReading _reading;
    private double _smoothedStrength;
    private long _lastPacketTimestamp;

    public DirtRally2TelemetryAdapter(int port = DefaultPort)
    {
        _port = port is > 0 and <= 65535 ? port : DefaultPort;
        _reading = Waiting("Listening for DiRT Rally 2.0 telemetry.");

        try
        {
            _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
            _listener = ListenAsync(_stopping.Token);
        }
        catch (SocketException)
        {
            _reading = Waiting($"Port {_port} is already in use. Close the other telemetry app or change its port.");
        }
    }

    public GameTelemetryReading Reading
    {
        get
        {
            var reading = Volatile.Read(ref _reading);
            var lastPacketTimestamp = Interlocked.Read(ref _lastPacketTimestamp);
            if (lastPacketTimestamp == 0
                || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastPacketTimestamp > StaleAfter.TotalMilliseconds)
            {
                return reading with
                {
                    Available = false,
                    Active = false,
                    Strength = 0,
                    Direction = 0,
                    Status = lastPacketTimestamp == 0
                        ? reading.Status
                        : "Telemetry stopped. Start a stage or check the DiRT Rally 2.0 UDP setup."
                };
            }

            return reading;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (_client is null) return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await _client.ReceiveAsync(cancellationToken);
                if (packet.Buffer.Length < PacketLength)
                {
                    Debug.WriteLine($"SteerCast: ignored DiRT Rally 2.0 telemetry packet with {packet.Buffer.Length} bytes.");
                    continue;
                }

                UpdateReading(packet.Buffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void UpdateReading(ReadOnlySpan<byte> packet)
    {
        // Standard extradata=3 packet: 66 little-endian floats.
        var lateralG = ReadFloat(packet, 34);
        var longitudinalG = ReadFloat(packet, 35);
        var suspensionMotion = Average(
            Math.Abs(ReadFloat(packet, 21)), Math.Abs(ReadFloat(packet, 22)),
            Math.Abs(ReadFloat(packet, 23)), Math.Abs(ReadFloat(packet, 24)));

        if (!double.IsFinite(lateralG) || !double.IsFinite(longitudinalG) || !double.IsFinite(suspensionMotion)) return;

        var lateralLoad = Math.Clamp(Math.Abs(lateralG) / 1.6, 0, 1);
        var longitudinalLoad = Math.Clamp(Math.Abs(longitudinalG) / 2.0, 0, 1);
        var roadLoad = Math.Clamp(suspensionMotion / 6.0, 0, 1);
        var unsmoothed = Math.Clamp(lateralLoad * 0.72 + longitudinalLoad * 0.16 + roadLoad * 0.12, 0, 1);
        _smoothedStrength += (unsmoothed - _smoothedStrength) * 0.28;

        var active = _smoothedStrength >= ActiveThreshold;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Volatile.Write(ref _reading, new GameTelemetryReading(
            "dirt-rally-2-udp", "DiRT Rally 2.0", "derived-telemetry", "Vehicle load",
            true, active, active ? Math.Round(_smoothedStrength, 3) : 0,
            active ? Math.Sign(lateralG) : 0, now, _port,
            "Receiving local game telemetry. This signal is derived and is not wheel force feedback."));
        Interlocked.Exchange(ref _lastPacketTimestamp, now);
    }

    private GameTelemetryReading Waiting(string status) => new(
        "dirt-rally-2-udp", "DiRT Rally 2.0", "derived-telemetry", "Vehicle load",
        false, false, 0, 0, 0, _port, status);

    private static double ReadFloat(ReadOnlySpan<byte> packet, int index) =>
        BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(index * sizeof(float), sizeof(float)));

    private static double Average(params double[] values) => values.Sum() / values.Length;

    public void Dispose()
    {
        _stopping.Cancel();
        _client?.Dispose();
        if (_listener is not null)
        {
            try { _listener.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        _stopping.Dispose();
    }
}
