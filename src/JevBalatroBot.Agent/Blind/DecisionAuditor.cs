using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Agent.Blind;

/// <summary>Records deviations from Jev's declared plan without changing its selected action.</summary>
public sealed class DecisionAuditor
{
    public IReadOnlyList<DecisionAuditFinding> Audit(BlindDecisionState state, IReadOnlyList<GameAction> candidates, GameAction selected)
    {
        var findings = new List<DecisionAuditFinding>();
        switch (state.JevTacticalIntent)
        {
            case "draw_for_build" when selected.Type != GameActionType.Discard:
                findings.Add(new DecisionAuditFinding("plan_action_divergence", "critical", "Jev planned to draw for the build but selected a non-discard action."));
                break;
            case "setup" when selected.Type != GameActionType.UseConsumable:
                findings.Add(new DecisionAuditFinding("plan_action_divergence", "critical", "Jev planned setup but selected a non-consumable action."));
                break;
            case "score_now" when selected.Type != GameActionType.PlayHand:
                findings.Add(new DecisionAuditFinding("plan_action_divergence", "critical", "Jev planned to score now but selected a non-play action."));
                break;
        }

        if (!string.IsNullOrWhiteSpace(state.JevTacticalTargetHand)
            && selected.Type == GameActionType.PlayHand
            && !string.Equals(selected.DetectedHand, state.JevTacticalTargetHand, StringComparison.Ordinal))
        {
            findings.Add(new DecisionAuditFinding("target_hand_divergence", "warning", $"Jev targeted {state.JevTacticalTargetHand} but played {selected.DetectedHand}."));
        }

        if (selected.Type == GameActionType.PlayHand
            && selected.RemainingBlindGap is > 0
            && state.Run.HandsRemaining > 1
            && candidates.Any(action => action.Type == GameActionType.Discard))
        {
            findings.Add(new DecisionAuditFinding("risk_play_with_discard_available", "warning", "The selected play does not clear the blind by its vanilla preview while a discard and another hand remain."));
        }

        return findings;
    }
}
