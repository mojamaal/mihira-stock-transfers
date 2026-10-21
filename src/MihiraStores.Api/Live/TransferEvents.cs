using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Transfers;

namespace MihiraStores.Api.Live;

/// <param name="Origin">The client that made the change (its X-Client-Id), so that client can ignore its own echo.</param>
public sealed record TransferChanged(
    int TransferId,
    TransferStatus Status,
    BranchRef FromBranch,
    BranchRef ToBranch,
    string? Origin,
    DateTimeOffset At);

/// <summary>
/// Fans transfer changes out to every connected screen. In-process, so it suits a single API instance;
/// several instances would need a shared backplane (e.g. Redis pub/sub or SQL Server change notifications).
/// </summary>
public sealed class TransferEvents
{
    public const string ClientIdHeader = "X-Client-Id";
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Guid, Channel<TransferChanged>> _subscribers = new();

    /// <summary>Publishes a change that actually happened. Replays changed nothing, so they are not announced.</summary>
    public void Publish(CommandResult result, string? origin)
    {
        if (result.Replayed)
        {
            return;
        }

        var t = result.Transfer;
        var change = new TransferChanged(t.Id, t.Status, t.FromBranch, t.ToBranch, origin, DateTimeOffset.UtcNow);
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(change); // a slow client drops its oldest events, never blocks a command
        }
    }

    /// <summary>
    /// One client's stream: a "ready" event straight away (the client refetches then, catching up on anything
    /// missed while disconnected), each change as a "transfer" event, and a "ping" every 15 s so idle proxies
    /// keep the connection open.
    /// </summary>
    public async IAsyncEnumerable<SseItem<TransferChanged?>> StreamAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TransferChanged>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _subscribers[id] = channel;

        try
        {
            yield return new SseItem<TransferChanged?>(null, "ready") { ReconnectionInterval = TimeSpan.FromSeconds(3) };

            while (!ct.IsCancellationRequested)
            {
                var hasChange = await WaitForChangeAsync(channel.Reader, ct);
                if (ct.IsCancellationRequested)
                {
                    yield break;
                }

                if (!hasChange)
                {
                    yield return new SseItem<TransferChanged?>(null, "ping");
                    continue;
                }

                while (channel.Reader.TryRead(out var change))
                {
                    yield return new SseItem<TransferChanged?>(change, "transfer");
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    /// <summary>True when a change is waiting; false after the heartbeat interval passes (or the client leaves).</summary>
    private static async Task<bool> WaitForChangeAsync(ChannelReader<TransferChanged> reader, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Heartbeat);
        try
        {
            return await reader.WaitToReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

public static class LiveEndpoints
{
    public static void MapLiveEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/events", (TransferEvents events, CancellationToken ct) =>
                TypedResults.ServerSentEvents(events.StreamAsync(ct)))
            .WithTags("Live");
}
