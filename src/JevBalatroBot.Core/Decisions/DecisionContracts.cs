using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Core.Decisions;

public interface IDecisionService
{
    Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken);
    Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken);
    Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken);
}

public enum DecisionSource { Jev, Fallback }

public sealed record BuildPlanDecision(string? BuildIntent, string? RawResponse, double? Confidence, string? Model);
public sealed record TacticalPlanDecision(string? TacticalIntent, string? TargetHand, int? ExpectedHandsToClear, string? RawResponse, double? Confidence, string? Model);
public sealed record DecisionResult(string? ActionId, string? RawResponse, double? Confidence, string? Model);
public sealed record DecisionAuditFinding(string Code, string Severity, string Summary);

public sealed record DecisionRecord(
    DateTimeOffset Timestamp,
    string RunId,
    int Ante,
    ActiveBlind Blind,
    BlindDecisionState GameState,
    IReadOnlyList<GameAction> CandidateActions,
    string? JevPlanResponse,
    double? PlanConfidence,
    string? JevTacticalResponse,
    double? TacticalConfidence,
    string? JevTacticalTargetHand,
    int? JevExpectedHandsToClear,
    string? JevResponse,
    double? ActionConfidence,
    GameAction SelectedAction,
    DecisionSource DecisionSource,
    long DecisionLatencyMs,
    int ResultingScore,
    int HandsRemaining,
    int DiscardsRemaining,
    IReadOnlyList<DecisionAuditFinding> AuditFindings);

public enum RunOutcome { Win, Loss, Error, Aborted }

public sealed record RunResult(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int HighestAnte,
    string HighestBlind,
    int FinalScore,
    int TotalDecisions,
    double AverageDecisionLatencyMs,
    int JevDecisions,
    int FallbackDecisions,
    RunOutcome Result,
    string? Detail);
