using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using SteerCast.Core;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

public sealed class InputBroadcaster(
    IWheelInputSource inputSource,
    ProfileStore profileStore,
    GameIntegrationManager? gameIntegrations = null) : IAsyncDisposable
{
    private static readonly long HeartbeatInterval = Stopwatch.Frequency / 2;
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = [];
    private readonly ConcurrentDictionary<string, PublishCadence> _cadences = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;
    private int _stopped;
    private long _sequence;

    public int ClientCount => _subscriptions.Count;
    public void Start() => _worker ??= ExecuteAsync(_stopping.Token);

    public SubscriptionHandle Subscribe(string profileId)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<InputFrame>(new BoundedChannelOptions(2)
        {
            // OBS only needs the newest state; dropping stale frames keeps latency bounded.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        _subscriptions[id] = new Subscription(profileId, channel, new FrameChangeDetector());
        return new SubscriptionHandle(channel.Reader, () => _subscriptions.TryRemove(id, out _));
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRefresh = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_subscriptions.IsEmpty)
            {
                // Suspend high-frequency device reads until setup or overlay clients reconnect.
                await Task.Delay(100, stoppingToken);
                continue;
            }

            if (DateTimeOffset.UtcNow - lastRefresh > TimeSpan.FromSeconds(2))
            {
                inputSource.Refresh();
                lastRefresh = DateTimeOffset.UtcNow;
            }

            var grouped = _subscriptions.ToArray().GroupBy(pair => pair.Value.ProfileId);
            foreach (var group in grouped)
            {
                var profile = await profileStore.GetAsync(group.Key, stoppingToken);
                if (profile is null)
                {
                    continue;
                }

                var frame = inputSource.Read(profile, Interlocked.Increment(ref _sequence));
                if (gameIntegrations is not null)
                {
                    frame = gameIntegrations.Apply(frame);
                }
                var cadence = _cadences.GetOrAdd(group.Key, _ => new PublishCadence());
                var now = Stopwatch.GetTimestamp();
                var interval = Stopwatch.Frequency / Math.Clamp(profile.FramesPerSecond, 1, 120);
                if (cadence.LastPublishedAt != 0 && now - cadence.LastPublishedAt < interval)
                {
                    continue;
                }

                cadence.LastPublishedAt = now;
                foreach (var pair in group)
                {
                    var subscription = pair.Value;
                    var changed = subscription.Detector.HasChanged(frame);
                    var heartbeatDue = subscription.LastDeliveredAt == 0 || now - subscription.LastDeliveredAt >= HeartbeatInterval;
                    if (changed || heartbeatDue)
                    {
                        subscription.LastDeliveredAt = now;
                        subscription.Channel.Writer.TryWrite(frame);
                    }
                }
            }

            await Task.Delay(4, stoppingToken);
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Channel.Writer.TryComplete();
        }

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
    }

    private sealed class Subscription(string profileId, Channel<InputFrame> channel, FrameChangeDetector detector)
    {
        public string ProfileId { get; } = profileId;
        public Channel<InputFrame> Channel { get; } = channel;
        public FrameChangeDetector Detector { get; } = detector;
        public long LastDeliveredAt { get; set; }
    }
    private sealed class PublishCadence
    {
        public long LastPublishedAt;
    }

    public sealed class SubscriptionHandle(ChannelReader<InputFrame> reader, Action dispose) : IDisposable
    {
        public ChannelReader<InputFrame> Reader { get; } = reader;
        public void Dispose() => dispose();
    }
}
