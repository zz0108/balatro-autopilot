using System.Net;
using System.Text;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;
using JevBalatroBot.Jev.Client;

namespace JevBalatroBot.Tests;

public sealed class JevDecisionServiceTests
{
    [Fact]
    public async Task PlanAsync_RequestsAndReturnsAConcreteBuildIntent()
    {
        var handler = new RecordingHandler("""
            {"model":"jev-1.13.0","answers":{"build_intent":{"type":"choice","choice":"Flush","confidence":0.8}}}
            """);
        using var client = new HttpClient(handler);
        var service = new JevDecisionService(client, new JevOptions(new Uri("https://example.test/v1/systemone"), "jev-1.13.0", "test-key"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(4, 4, 3), new HandState(5, []), [], [], [new PokerHandState("Flush", 1, 35, 4)]) { Phase = "SELECTING_HAND" };

        var result = await service.PlanAsync(state, CancellationToken.None);

        Assert.Equal("Flush", result.BuildIntent);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("build_intent", out _));
        Assert.False(request.RootElement.GetProperty("questions").TryGetProperty("action", out _));
    }

    [Fact]
    public async Task DecideAsync_UsesThePlannedBuildAndOnlyRequestsAnAction()
    {
        var handler = new RecordingHandler("""
            {"model":"jev-1.13.0","answers":{"action":{"type":"choice","choice":"action_001","confidence":0.9}}}
            """);
        using var client = new HttpClient(handler);
        var service = new JevDecisionService(client, new JevOptions(new Uri("https://example.test/v1/systemone"), "jev-1.13.0", "test-key"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(4, 4, 3), new HandState(5, []), [], [], []) { Phase = "SELECTING_HAND", JevBuildIntent = "Flush", JevTacticalIntent = "draw_for_build" };
        var action = new GameAction("action_001", GameActionType.PlayHand, "Play a card.", [0], [1], "High Card");

        var result = await service.DecideAsync(state, [action], CancellationToken.None);

        Assert.Equal("action_001", result.ActionId);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Flush", request.RootElement.GetProperty("state").GetProperty("JevBuildIntent").GetString());
        Assert.Equal("draw_for_build", request.RootElement.GetProperty("state").GetProperty("JevTacticalIntent").GetString());
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("action", out _));
        Assert.False(request.RootElement.GetProperty("questions").TryGetProperty("build_intent", out _));
        var instructions = request.RootElement.GetProperty("questions").GetProperty("action").GetProperty("instructions").GetString();
        Assert.Contains("Base preview", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanTacticsAsync_ProvidesLegalCandidatesBeforeChoosingADrawIntent()
    {
        var handler = new RecordingHandler("""
            {"model":"jev-1.13.0","answers":{"tactical_intent":{"type":"choice","choice":"draw_for_build","confidence":0.7},"target_hand":{"type":"choice","choice":"Flush","confidence":0.8},"expected_hands":{"type":"choice","choice":"clear_in_2","confidence":0.6}}}
            """);
        using var client = new HttpClient(handler);
        var service = new JevDecisionService(client, new JevOptions(new Uri("https://example.test/v1/systemone"), "jev-1.13.0", "test-key"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 120), new RunResources(4, 4, 3), new HandState(5, []), [], [], []) { Phase = "SELECTING_HAND", JevBuildIntent = "Flush" };
        var actions = new[]
        {
            new GameAction("play_001", GameActionType.PlayHand, "Play: Base preview: +40.", [0], [1], "High Card"),
            new GameAction("discard_001", GameActionType.Discard, "Discard: redraw for a later hand.", [0], [1], "Discard")
        };

        var result = await service.PlanTacticsAsync(state, actions, CancellationToken.None);

        Assert.Equal("draw_for_build", result.TacticalIntent);
        Assert.Equal("Flush", result.TargetHand);
        Assert.Equal(2, result.ExpectedHandsToClear);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("tactical_intent", out _));
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("target_hand", out _));
        Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("expected_hands", out _));
        Assert.Equal("Discard: redraw for a later hand.", request.RootElement.GetProperty("state").GetProperty("candidate_actions")[1].GetProperty("Description").GetString());
    }

    [Fact]
    public async Task DecideAsync_ShopDoesNotSendHandTacticsOrRunMemory()
    {
        var handler = new RecordingHandler("""
            {"model":"jev-1.13.0","answers":{"action":{"type":"choice","choice":"buy_001","confidence":0.9}}}
            """);
        using var client = new HttpClient(handler);
        var service = new JevDecisionService(client, new JevOptions(new Uri("https://example.test/v1/systemone"), "jev-1.13.0", "test-key"));
        var state = new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(14, 4, 3), new HandState(5, []), [], [], [])
        {
            Phase = "SHOP",
            JevBuildIntent = "Full House",
            JevTacticalIntent = "draw_for_build",
            JevTacticalTargetHand = "Full House",
            JevExpectedHandsToClear = 3,
            JevRunMemory = [new RunMemoryEntry(1, 1, "Small Blind", 0, 300, "Full House", "draw_for_build", "Full House", 3, "Discard", "Discard", [1], null)]
        };

        var result = await service.DecideAsync(state, [new GameAction("buy_001", GameActionType.BuyShopCard, "Buy Drunkard for $4.", [], [], "")], CancellationToken.None);

        Assert.Equal("buy_001", result.ActionId);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        var requestState = request.RootElement.GetProperty("state");
        Assert.Equal("Full House", requestState.GetProperty("JevBuildIntent").GetString());
        Assert.Equal(JsonValueKind.Null, requestState.GetProperty("JevTacticalIntent").ValueKind);
        Assert.Equal(0, requestState.GetProperty("JevRunMemory").GetArrayLength());
        var instructions = request.RootElement.GetProperty("questions").GetProperty("action").GetProperty("instructions").GetString();
        Assert.DoesNotContain("draw_for_build", instructions, StringComparison.Ordinal);
        Assert.Contains("reroll-triggered", instructions, StringComparison.Ordinal);
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
