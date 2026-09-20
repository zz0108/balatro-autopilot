using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

internal sealed class FakeJevDecisionService(string actionId) : IDecisionService
{
    public Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken)
        => Task.FromResult(new BuildPlanDecision(state.PokerHands.First().Name, "fake-plan", 1, "fake-jev"));

    public Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
        => Task.FromResult(new TacticalPlanDecision("score_now", state.PokerHands.First().Name, 1, "fake-tactical", 1, "fake-jev"));

    public Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
        => Task.FromResult(new DecisionResult(actionId, "fake", 1, "fake-jev"));
}
