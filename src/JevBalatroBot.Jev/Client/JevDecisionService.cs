using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Jev.Client;

public interface IJevDecisionService : IDecisionService;

public sealed class JevDecisionService(HttpClient httpClient, JevOptions options) : IJevDecisionService
{
    private const string Objective = "Act as the sole Balatro strategist for the full run. Follow the current JevBuildIntent, JevTacticalIntent, JevTacticalTargetHand, and JevExpectedHandsToClear while balancing the blind, money, Jokers, consumables, shop offers, and booster packs. This is your own binding plan: choose the legal action that realizes it, unless the game state has changed or an immediate win makes a different action necessary. For draw_for_build, choose a Discard; for setup, choose UseConsumable; for score_now, choose a PlayHand and normally the chosen target hand. Blind.Effect and BlindConstraints are mandatory game rules, not background information. Every PlayHand criterion includes a Base preview: it is a reliable vanilla score floor that excludes Jokers and card effects, not the final score. Use its resulting blind gap, remaining hands, and Build MATCH/MISMATCH signal to choose a survival plan; discard and consumable criteria explicitly state when they score nothing immediately. JevRunMemory contains only earlier turns in this run: use it to avoid repeating a failed line, not as a reason to ignore the current state. Choose exactly one action from the provided legal actions. Do not defer to an external heuristic; make the strategic decision yourself.";
    private const string BuildObjective = "Choose exactly one long-term poker-hand build from the available hand types. Consider current cards, scoring levels, Jokers, consumables, shop offers, booster packs, money, and the active blind. Do not choose an uncommitted or flexible option; a fresh plan is chosen before every action.";
    private const string TacticalObjective = "Choose a committed tactical plan for this hand only. score_now means play a hand now because it can clear or materially advance the blind. draw_for_build means use an available discard now to improve the current build or form a stronger hand before spending another hand. setup means use an available consumable now to create a later scoring hand. TargetHand names the hand you intend to score or draw toward. ExpectedHandsToClear is your estimate from this state, including the current action. Evaluate the complete candidate list, blind gap, hands, discards, JevBuildIntent, and earlier in-run memory. Do not choose score_now merely because a play scores immediately when it leaves the blind far short and a draw path is stronger.";
    private const string ShopObjective = "Act as the sole Balatro shop strategist. Evaluate every legal purchase, voucher, booster, sale, and reroll against current money, Joker slots, consumable slots, active Build intent, and future blind survival. Before choosing, inspect every current Joker description for reroll-triggered effects. When a Joker grows or triggers on a shop reroll, include that immediate upgrade and the remaining money after the reroll in the comparison; do not spend below reroll cost on a weaker purchase without first deciding whether the reroll-triggered value is better. Buy a materially useful option when it improves the run at a sensible price; do not leave merely to preserve money. Leave only when no current option, reroll, sale, or pack is worth taking. Choose exactly one legal action; make the strategic decision yourself.";

    public async Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new JevDecisionException("JEV_API_KEY is not configured.");
        }

        var criteria = state.PokerHands.ToDictionary(
            hand => hand.Name,
            hand => $"Level {hand.Level}: {hand.BaseChips} base chips × {hand.BaseMult} base mult.",
            StringComparer.Ordinal);
        if (criteria.Count == 0)
        {
            throw new JevDecisionException("The game state contains no available poker-hand build options.");
        }

        var payload = new
        {
            model = options.Model,
            state,
            questions = new Dictionary<string, object>
            {
                ["build_intent"] = new { type = "choice", instructions = BuildObjective, criteria }
            }
        };
        using var document = await SendAsync(payload, cancellationToken);
        var answer = document.RootElement.GetProperty("answers").GetProperty("build_intent");
        var buildIntent = answer.GetProperty("choice").GetString();
        return new BuildPlanDecision(
            criteria.ContainsKey(buildIntent ?? string.Empty) ? buildIntent : null,
            document.RootElement.GetRawText(),
            answer.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : null,
            document.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null);
    }

    public async Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new JevDecisionException("JEV_API_KEY is not configured.");
        }

        var criteria = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["score_now"] = $"Play a scoring hand now. {actions.Count(action => action.Type == GameActionType.PlayHand)} PlayHand candidates are available."
        };
        if (actions.Any(action => action.Type == GameActionType.Discard))
        {
            criteria["draw_for_build"] = $"Discard now to redraw toward JevBuildIntent '{state.JevBuildIntent ?? "none"}'. {actions.Count(action => action.Type == GameActionType.Discard)} Discard candidates are available.";
        }
        if (actions.Any(action => action.Type == GameActionType.UseConsumable))
        {
            criteria["setup"] = $"Use a consumable now to improve a later scoring hand. {actions.Count(action => action.Type == GameActionType.UseConsumable)} consumable candidates are available.";
        }

        var targetCriteria = actions
            .Where(action => action.Type == GameActionType.PlayHand)
            .Select(action => action.DetectedHand)
            .Append(state.JevBuildIntent)
            .Where(hand => !string.IsNullOrWhiteSpace(hand))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(hand => hand!, hand => $"Target this {hand} line. Use the legal candidate cards to realize it.", StringComparer.Ordinal);
        var expectedHandsCriteria = Enumerable.Range(1, Math.Max(1, state.Run.HandsRemaining))
            .ToDictionary(count => $"clear_in_{count}", count => count == 1 ? "Clear the blind with this hand." : $"Clear the blind within {count} played hands, including the next play.", StringComparer.Ordinal);

        var payload = new
        {
            model = options.Model,
            state = new { game_state = state, candidate_actions = actions },
            questions = new Dictionary<string, object>
            {
                ["tactical_intent"] = new { type = "choice", instructions = TacticalObjective, criteria },
                ["target_hand"] = new { type = "choice", instructions = TacticalObjective, criteria = targetCriteria },
                ["expected_hands"] = new { type = "choice", instructions = TacticalObjective, criteria = expectedHandsCriteria }
            }
        };
        using var document = await SendAsync(payload, cancellationToken);
        var answers = document.RootElement.GetProperty("answers");
        var answer = answers.GetProperty("tactical_intent");
        var intent = answer.GetProperty("choice").GetString();
        var targetAnswer = answers.GetProperty("target_hand");
        var targetHand = targetAnswer.GetProperty("choice").GetString();
        var expectedAnswer = answers.GetProperty("expected_hands");
        var expectedChoice = expectedAnswer.GetProperty("choice").GetString();
        var expectedHandsToClear = expectedChoice is not null && expectedHandsCriteria.ContainsKey(expectedChoice)
            && int.TryParse(expectedChoice["clear_in_".Length..], out var expectedHands)
            ? (int?)expectedHands
            : null;
        return new TacticalPlanDecision(
            criteria.ContainsKey(intent ?? string.Empty) ? intent : null,
            targetCriteria.ContainsKey(targetHand ?? string.Empty) ? targetHand : null,
            expectedHandsToClear,
            document.RootElement.GetRawText(),
            answer.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : null,
            document.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null);
    }

    public async Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new JevDecisionException("JEV_API_KEY is not configured.");
        }

        var criteria = actions.ToDictionary(action => action.Id, action => action.Description, StringComparer.Ordinal);
        var isShopDecision = state.Phase == "SHOP";
        var decisionState = isShopDecision
            ? state with
            {
                JevTacticalIntent = null,
                JevTacticalTargetHand = null,
                JevExpectedHandsToClear = null,
                JevRunMemory = []
            }
            : state;
        var payload = new
        {
            model = options.Model,
            state = decisionState,
            questions = new Dictionary<string, object>
            {
                ["action"] = new { type = "choice", instructions = isShopDecision ? ShopObjective : Objective, criteria }
            }
        };
        using var document = await SendAsync(payload, cancellationToken);
        var answer = document.RootElement.GetProperty("answers").GetProperty("action");
        return new DecisionResult(
            answer.GetProperty("choice").GetString(),
            document.RootElement.GetRawText(),
            answer.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : null,
            document.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null);
    }

    private async Task<JsonDocument> SendAsync<TPayload>(TPayload payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new JevDecisionException($"Jev returned HTTP {(int)response.StatusCode}: {body}");
        }
        return JsonDocument.Parse(body);
    }
}

public sealed record JevOptions(Uri Endpoint, string Model, string? ApiKey);
public sealed class JevDecisionException(string message) : Exception(message);
