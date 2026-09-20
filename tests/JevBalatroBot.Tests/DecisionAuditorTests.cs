using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class DecisionAuditorTests
{
    [Fact]
    public void Audit_ReportsWhenJevsDrawPlanIsNotFollowed()
    {
        var state = new BlindDecisionState(1, new ActiveBlind("BIG", "Big Blind", "", 450, 312), new RunResources(4, 3, 2), new HandState(5, []), [], [], [])
        {
            Phase = "SELECTING_HAND",
            JevTacticalIntent = "draw_for_build",
            JevTacticalTargetHand = "Flush"
        };
        var selected = new GameAction("play_001", GameActionType.PlayHand, "Play", [0], [1], "Pair") { RemainingBlindGap = 78 };
        var candidates = new[]
        {
            selected,
            new GameAction("discard_001", GameActionType.Discard, "Discard", [2], [3], "Discard")
        };

        var findings = new DecisionAuditor().Audit(state, candidates, selected);

        Assert.Contains(findings, finding => finding.Code == "plan_action_divergence");
        Assert.Contains(findings, finding => finding.Code == "target_hand_divergence");
        Assert.Contains(findings, finding => finding.Code == "risk_play_with_discard_available");
    }
}
