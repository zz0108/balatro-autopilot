using System.Text;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Jev.Client;

public sealed class OllamaDecisionService(HttpClient httpClient, OllamaOptions options) : IDecisionService
{
    private const string BuildSystemPrompt = "You are the autonomous strategist for a Balatro run. Analyze the supplied game state carefully, including cards, scoring levels, Jokers, money, consumables, shop offers, packs, blind constraints, and the current blind. Choose one concrete build from the allowed buildIntent values. Think before responding, but return only JSON matching the required schema.";
    private const string TacticsSystemPrompt = "You are the autonomous tactical planner for the current Balatro hand. Analyze the full game state and legal candidate actions. Decide whether to score now, discard to improve the hand, or set up with a consumable; select a concrete target poker hand and realistic number of played hands to clear the blind. Respect mandatory blind constraints. Think before responding, but return only JSON matching the required schema.";
    private const string ActionSystemPrompt = "You are the autonomous Balatro player. Analyze the full game state, current build, tactical plan, Jokers, consumables, money, blind constraints, and every legal candidate action. Select the single action ID that gives the best chance of winning the run. In a shop, account for synergies and triggers on held Jokers, including effects activated by rerolling. You may only select a supplied legal action ID. Think before responding, but return only JSON matching the required schema.";

    public async Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken)
    {
        var allowed = state.PokerHands.Select(hand => hand.Name).Distinct(StringComparer.Ordinal).ToArray();
        if (allowed.Length == 0) throw new OllamaDecisionException("The game state contains no available poker-hand build options.");

        var response = await ChatAsync(
            BuildSystemPrompt,
            new { game_state = state, allowed_builds = allowed },
            ChoiceSchema("buildIntent", allowed),
            cancellationToken);
        using var answer = JsonDocument.Parse(response.Content);
        var buildIntent = GetString(answer.RootElement, "buildIntent");
        return new BuildPlanDecision(allowed.Contains(buildIntent, StringComparer.Ordinal) ? buildIntent : null, response.RawResponse, null, response.Model);
    }

    public async Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
    {
        var intents = new List<string> { "score_now" };
        if (actions.Any(action => action.Type == GameActionType.Discard)) intents.Add("draw_for_build");
        if (actions.Any(action => action.Type == GameActionType.UseConsumable)) intents.Add("setup");
        var targetHands = actions
            .Where(action => action.Type == GameActionType.PlayHand)
            .Select(action => action.DetectedHand)
            .Append(state.JevBuildIntent)
            .Where(hand => !string.IsNullOrWhiteSpace(hand))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
        if (targetHands.Length == 0) throw new OllamaDecisionException("The game state contains no playable poker-hand target.");

        var response = await ChatAsync(
            TacticsSystemPrompt,
            new { game_state = state, candidate_actions = actions, tactical_intents = intents, target_hands = targetHands },
            TacticalSchema(intents, targetHands, Math.Max(1, state.Run.HandsRemaining)),
            cancellationToken);
        using var answer = JsonDocument.Parse(response.Content);
        var intent = GetString(answer.RootElement, "tacticalIntent");
        var targetHand = GetString(answer.RootElement, "targetHand");
        var expectedHands = answer.RootElement.TryGetProperty("expectedHandsToClear", out var expected) && expected.TryGetInt32(out var count) ? count : 0;
        return new TacticalPlanDecision(
            intents.Contains(intent, StringComparer.Ordinal) ? intent : null,
            targetHands.Contains(targetHand, StringComparer.Ordinal) ? targetHand : null,
            expectedHands is >= 1 and <= int.MaxValue && expectedHands <= state.Run.HandsRemaining ? expectedHands : null,
            response.RawResponse,
            null,
            response.Model);
    }

    public async Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
    {
        if (actions.Count == 0) throw new OllamaDecisionException("No legal actions were supplied to Ollama.");
        var actionIds = actions.Select(action => action.Id).ToArray();
        var response = await ChatAsync(
            ActionSystemPrompt,
            new { game_state = state, candidate_actions = actions },
            ChoiceSchema("actionId", actionIds),
            cancellationToken);
        using var answer = JsonDocument.Parse(response.Content);
        var actionId = GetString(answer.RootElement, "actionId");
        return new DecisionResult(actionIds.Contains(actionId, StringComparer.Ordinal) ? actionId : null, response.RawResponse, null, response.Model);
    }

    private async Task<OllamaResponse> ChatAsync(string systemPrompt, object state, object schema, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = options.Model,
            stream = false,
            think = true,
            format = schema,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = JsonSerializer.Serialize(state) }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new OllamaDecisionException($"Ollama returned HTTP {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var message = document.RootElement.GetProperty("message");
        var content = message.GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new OllamaDecisionException("Ollama returned an empty JSON decision.");
        var model = document.RootElement.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : options.Model;
        return new OllamaResponse(content, body, model);
    }

    private static object ChoiceSchema(string property, IReadOnlyList<string> values)
        => new
        {
            type = "object",
            properties = new Dictionary<string, object> { [property] = new { type = "string", @enum = values } },
            required = new[] { property },
            additionalProperties = false
        };

    private static object TacticalSchema(IReadOnlyList<string> intents, IReadOnlyList<string> targetHands, int maximumHands)
        => new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["tacticalIntent"] = new { type = "string", @enum = intents },
                ["targetHand"] = new { type = "string", @enum = targetHands },
                ["expectedHandsToClear"] = new { type = "integer", minimum = 1, maximum = maximumHands }
            },
            required = new[] { "tacticalIntent", "targetHand", "expectedHandsToClear" },
            additionalProperties = false
        };

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record OllamaResponse(string Content, string RawResponse, string? Model);
}

public sealed record OllamaOptions(Uri Endpoint, string Model);
public sealed class OllamaDecisionException(string message) : Exception(message);
