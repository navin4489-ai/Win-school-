using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;

namespace SchoolErp.Infrastructure.Services;

/// <summary>
/// In-memory fan-out hub for live notifications.
///
/// Every persisted notification is published here as soon as it is written, which lets
/// the web portal raise a toast and play a bell without waiting for the 15 second
/// outbound dispatcher. Subscribers are matched on the user account or the guardian the
/// notification was addressed to, mirroring the rules used when reading the inbox.
///
/// The hub is process-local. A multi-node deployment must fan out through Redis
/// pub/sub or an Azure SignalR Service instead; the interface is the seam for that.
/// </summary>
public sealed class NotificationHub : INotificationPublisher
{
    private const int MaxQueuedPerSubscriber = 50;

    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger) => _logger = logger;

    public void Publish(Notification notification)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (!subscription.Matches(notification)) continue;

            // Drop the oldest item when a slow client falls behind rather than growing
            // the channel without bound.
            if (subscription.Channel.Reader.Count >= MaxQueuedPerSubscriber)
            {
                subscription.Channel.Reader.TryRead(out _);
                _logger.LogWarning("Notification subscriber {Id} is lagging; dropped an older item.",
                    subscription.Id);
            }

            subscription.Channel.Writer.TryWrite(notification);
        }
    }

    /// <summary>Registers a live subscriber and returns a handle used to release it.</summary>
    public IDisposable Subscribe(int? userAccountId, int? guardianId, out ChannelReader<Notification> reader)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(MaxQueuedPerSubscriber)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var subscription = new Subscription(id, userAccountId, guardianId, channel);
        _subscriptions[id] = subscription;
        reader = channel.Reader;

        return new SubscriptionHandle(this, id);
    }

    private void Remove(Guid id) => _subscriptions.TryRemove(id, out _);

    private sealed class Subscription
    {
        public Subscription(Guid id, int? userAccountId, int? guardianId, Channel<Notification> channel)
        {
            Id = id;
            UserAccountId = userAccountId;
            GuardianId = guardianId;
            Channel = channel;
        }

        public Guid Id { get; }
        public int? UserAccountId { get; }
        public int? GuardianId { get; }
        public Channel<Notification> Channel { get; }

        public bool Matches(Notification notification)
        {
            if (UserAccountId is not null && notification.UserAccountId == UserAccountId) return true;
            if (GuardianId is not null && notification.GuardianId == GuardianId) return true;
            return false;
        }
    }

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly NotificationHub _hub;
        private readonly Guid _id;
        private bool _disposed;

        public SubscriptionHandle(NotificationHub hub, Guid id)
        {
            _hub = hub;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hub.Remove(_id);
        }
    }
}

/// <summary>No-op publisher used by tests and by hosts that do not expose a live stream.</summary>
public sealed class NullNotificationPublisher : INotificationPublisher
{
    public void Publish(Notification notification)
    {
    }
}
