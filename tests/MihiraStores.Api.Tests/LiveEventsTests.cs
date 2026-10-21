using System.Text.Json;

namespace MihiraStores.Api.Tests;

public sealed class LiveEventsTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Open_screens_are_told_about_each_change_once_with_who_made_it()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await Client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(timeout.Token));
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        await ReadEventAsync(reader, "ready", timeout.Token);

        var id = await CreateTransfer(Colombo, Kandy, (Rice, 2));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/transfers/{id}/dispatch");
        request.Headers.Add("X-Client-Id", "screen-a");
        await Client.SendAsync(request, timeout.Token);
        await Act(id, "dispatch"); // a replay: changes nothing, so it must not be announced

        var created = await ReadEventAsync(reader, "transfer", timeout.Token);
        var dispatched = await ReadEventAsync(reader, "transfer", timeout.Token);

        Assert.Equal((id, "Requested"), (created.GetProperty("transferId").GetInt32(), created.GetProperty("status").GetString()));
        Assert.Equal((id, "Dispatched", "screen-a"),
            (dispatched.GetProperty("transferId").GetInt32(), dispatched.GetProperty("status").GetString(), dispatched.GetProperty("origin").GetString()));
        Assert.Equal("Kandy", dispatched.GetProperty("toBranch").GetProperty("name").GetString());

        // Prove the replay was not announced: the next thing on the stream is the cancel below, not a second dispatch.
        var other = await CreateTransfer(Colombo, Galle, (Tea, 1));
        await ReadEventAsync(reader, "transfer", timeout.Token); // other's creation
        Assert.Equal(other, (await ReadEventAsync(reader, "transfer", timeout.Token, after: () => Act(other, "cancel"))).GetProperty("transferId").GetInt32());
    }

    /// <summary>Reads SSE lines until an event of the given type arrives, skipping heartbeats.</summary>
    private static async Task<JsonElement> ReadEventAsync(StreamReader reader, string type, CancellationToken ct, Func<Task>? after = null)
    {
        if (after is not null) await after();

        string? eventType = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: ")) eventType = line["event: ".Length..];
            else if (line.StartsWith("data:") && eventType == type)
            {
                var data = line["data:".Length..].Trim();
                return data.Length == 0 ? default : JsonDocument.Parse(data).RootElement; // "ready"/"ping" carry no data
            }
            else if (line.Length == 0) eventType = null;
        }

        throw new InvalidOperationException($"Stream ended before a '{type}' event.");
    }
}
