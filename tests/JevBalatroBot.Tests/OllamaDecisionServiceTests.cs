using System.Net;
using System.Text;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;
using JevBalatroBot.Jev.Client;

namespace JevBalatroBot.Tests;

public sealed class OllamaDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_UsesOllamaChatWithAConstrainedActionSchema()
    {
        var handler = new RecordingHandler("""
            {"model":"qwen3:14b","message":{"role":"assistant","content":"{\"actionId\":\"action_001\"}","thinking":"Compared the legal actions."},"done":true}
            """);
        using var client = new HttpClient(handler);
        var service = new OllamaDecisionService(client, new OllamaOptions(new Uri("http://127.0.0.1:11434/api/chat"), "qwen3:14b"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(4, 4, 3), new HandState(5, []), [], [], []) { Phase = "SELECTING_HAND" };

        var result = await service.DecideAsync(state, [new GameAction("action_001", GameActionType.PlayHand, "Play a Pair.", [0, 1], [1, 2], "Pair")], CancellationToken.None);

        Assert.Equal("action_001", result.ActionId);
        Assert.Equal("qwen3:14b", result.Model);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal("object", request.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("actionId", request.RootElement.GetProperty("format").GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task PlanTacticsAsync_ReturnsAValidatedStructuredPlan()
    {
        var handler = new RecordingHandler("""
            {"model":"qwen3:14b","message":{"role":"assistant","content":"{\"tacticalIntent\":\"draw_for_build\",\"targetHand\":\"Flush\",\"expectedHandsToClear\":2}"},"done":true}
            """);
        using var client = new HttpClient(handler);
        var service = new OllamaDecisionService(client, new OllamaOptions(new Uri("http://127.0.0.1:11434/api/chat"), "qwen3:14b"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(4, 3, 2), new HandState(5, []), [], [], []) { Phase = "SELECTING_HAND", JevBuildIntent = "Flush" };
        var actions = new[]
        {
            new GameAction("play_001", GameActionType.PlayHand, "Play a Flush.", [0, 1, 2, 3, 4], [1, 2, 3, 4, 5], "Flush"),
            new GameAction("discard_001", GameActionType.Discard, "Discard a card.", [0], [1], "Discard")
        };

        var result = await service.PlanTacticsAsync(state, actions, CancellationToken.None);

        Assert.Equal("draw_for_build", result.TacticalIntent);
        Assert.Equal("Flush", result.TargetHand);
        Assert.Equal(2, result.ExpectedHandsToClear);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("tacticalIntent", request.RootElement.GetProperty("format").GetProperty("required")[0].GetString());
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
