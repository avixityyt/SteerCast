using System.Diagnostics;
using System.Text.Json;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// Records a short, bounded sample from the existing input broadcast. It does
/// not add wheel or game polling and caps writes to 20 samples per second.
/// </summary>
public sealed class TelemetryCaptureService(
    InputBroadcaster broadcaster,
    GameIntegrationManager gameIntegrations,
    string? captureDirectory = null) : IAsyncDisposable
{
    private const int SampleIntervalMilliseconds = 50;
    private readonly object _sync = new();
    private readonly string _captureDirectory = captureDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteerCast",
        "captures");
    private CaptureSession? _active;
    private TelemetryCaptureStatus _status = new(
        false, 0, 30, 0, 0, null, "Ready to record a short local driving sample.");

    public TelemetryCaptureStatus Status
    {
        get
        {
            lock (_sync)
            {
                if (_active is null) return _status;
                var elapsed = DateTimeOffset.UtcNow - _active.StartedAt;
                var remaining = Math.Max(0, (int)Math.Ceiling(_active.Duration.TotalSeconds - elapsed.TotalSeconds));
                return _status with
                {
                    RemainingSeconds = remaining,
                    SampleCount = Volatile.Read(ref _active.SampleCount)
                };
            }
        }
    }

    public TelemetryCaptureStatus Start(TelemetryCaptureRequest request)
    {
        lock (_sync)
        {
            if (_active is not null) return Status;

            var profileId = string.IsNullOrWhiteSpace(request.ProfileId) ? "default" : request.ProfileId.Trim();
            var duration = TimeSpan.FromSeconds(Math.Clamp(request.DurationSeconds, 5, 120));
            var session = new CaptureSession(profileId, DateTimeOffset.UtcNow, duration);
            _active = session;
            _status = new TelemetryCaptureStatus(
                true,
                session.StartedAt.ToUnixTimeMilliseconds(),
                (int)duration.TotalSeconds,
                (int)duration.TotalSeconds,
                0,
                _status.LatestFile,
                "Recording existing wheel and game telemetry frames.");
            session.Worker = CaptureAsync(session);
            return _status;
        }
    }

    public async Task<TelemetryCaptureStatus> StopAsync()
    {
        CaptureSession? session;
        lock (_sync)
        {
            session = _active;
        }

        if (session is null) return Status;
        session.Cancellation.Cancel();
        await session.Worker;
        return Status;
    }

    private async Task CaptureAsync(CaptureSession session)
    {
        using var subscription = broadcaster.Subscribe(session.ProfileId);
        session.Cancellation.CancelAfter(session.Duration);
        var sampleClock = Stopwatch.StartNew();
        long lastSampleAt = -SampleIntervalMilliseconds;

        try
        {
            await foreach (var sourceFrame in subscription.Reader.ReadAllAsync(session.Cancellation.Token))
            {
                var elapsedMilliseconds = sampleClock.ElapsedMilliseconds;
                if (elapsedMilliseconds - lastSampleAt < SampleIntervalMilliseconds) continue;

                var frame = gameIntegrations.Apply(sourceFrame, includeWhenOverlayHidden: true);
                if (!frame.Connected || frame.GameTelemetryKind is null) continue;

                session.Samples.Add(new TelemetryCaptureSample(
                    frame.Timestamp,
                    elapsedMilliseconds,
                    frame.Steering,
                    frame.Throttle,
                    frame.Brake,
                    frame.Clutch,
                    frame.Handbrake,
                    frame.Gear,
                    frame.GameTelemetryStrength ?? 0,
                    frame.GameTelemetrySpeed,
                    frame.GameTelemetrySlipAngle,
                    frame.GameTelemetryYawRate,
                    frame.GameTelemetryKind,
                    frame.GameTelemetrySource ?? "unknown"));
                lastSampleAt = elapsedMilliseconds;
                Volatile.Write(ref session.SampleCount, session.Samples.Count);
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            await FinishAsync(session);
        }
    }

    private async Task FinishAsync(CaptureSession session)
    {
        string? savedPath = null;
        string message;
        if (session.Samples.Count == 0)
        {
            message = "No game telemetry was captured. Start a stage and try again.";
        }
        else
        {
            try
            {
                Directory.CreateDirectory(_captureDirectory);
                var endedAt = DateTimeOffset.UtcNow;
                var fileName = $"dirt-rally-2-{session.StartedAt:yyyyMMdd-HHmmss}.json";
                savedPath = Path.Combine(_captureDirectory, fileName);
                var temporaryPath = savedPath + ".tmp";
                var document = new TelemetryCaptureDocument(
                    1,
                    "dirt-rally-2",
                    session.StartedAt.ToUnixTimeMilliseconds(),
                    endedAt.ToUnixTimeMilliseconds(),
                    1000 / SampleIntervalMilliseconds,
                    session.Samples.ToArray());
                await using (var stream = File.Create(temporaryPath))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        AppJsonContext.Default.TelemetryCaptureDocument,
                        CancellationToken.None);
                }
                File.Move(temporaryPath, savedPath, true);
                message = $"Saved {session.Samples.Count} samples locally.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                message = $"Capture could not be saved: {exception.Message}";
            }
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_active, session)) return;
            _active = null;
            _status = new TelemetryCaptureStatus(
                false,
                session.StartedAt.ToUnixTimeMilliseconds(),
                (int)session.Duration.TotalSeconds,
                0,
                session.Samples.Count,
                savedPath ?? _status.LatestFile,
                message);
        }
        session.Cancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private sealed class CaptureSession(string profileId, DateTimeOffset startedAt, TimeSpan duration)
    {
        public string ProfileId { get; } = profileId;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public TimeSpan Duration { get; } = duration;
        public CancellationTokenSource Cancellation { get; } = new();
        public List<TelemetryCaptureSample> Samples { get; } = [];
        public int SampleCount;
        public Task Worker { get; set; } = Task.CompletedTask;
    }
}

public sealed record TelemetryCaptureRequest(string ProfileId = "default", int DurationSeconds = 30);

public sealed record TelemetryCaptureStatus(
    bool Recording,
    long StartedAt,
    int DurationSeconds,
    int RemainingSeconds,
    int SampleCount,
    string? LatestFile,
    string Message);

public sealed record TelemetryCaptureSample(
    long Timestamp,
    long ElapsedMilliseconds,
    double Steering,
    double Throttle,
    double Brake,
    double Clutch,
    double Handbrake,
    int Gear,
    double DerivedLoad,
    double Speed,
    double SlipAngle,
    double YawRate,
    string SignalKind,
    string Source);

public sealed record TelemetryCaptureDocument(
    int SchemaVersion,
    string GameId,
    long StartedAt,
    long EndedAt,
    int SampleRateLimitHz,
    TelemetryCaptureSample[] Samples);
