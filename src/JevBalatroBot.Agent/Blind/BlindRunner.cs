using System.Diagnostics;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Agent.Blind;

public interface IRunObserver { void OnProgress(string message, GameSnapshot snapshot); void OnDecision(DecisionRecord record); }

public sealed class BlindRunner(IGameGateway gameGateway, IDecisionService decisionService, CandidateActionGenerator actionGenerator, ActionValidator actionValidator, DecisionLogWriter logWriter, IRunObserver observer, FallbackActionSelector? fallbackActionSelector = null, DecisionAuditor? decisionAuditor = null)
{
    private const int MaxShopDecisions = 10;
    private readonly FallbackActionSelector _fallbackActionSelector = fallbackActionSelector ?? new FallbackActionSelector();
    private readonly DecisionAuditor _decisionAuditor = decisionAuditor ?? new DecisionAuditor();
    private string? _jevBuildIntent;
    private readonly List<RunMemoryEntry> _runMemory = [];

    public async Task<RunResult> RunAsync(NewRunOptions newRunOptions, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var latencies = new List<long>();
        var jevDecisions = 0;
        var fallbackDecisions = 0;
        var shopDecisions = 0;
        var runLog = logWriter.CreateRun(runId);
        _jevBuildIntent = null;
        _runMemory.Clear();
        var snapshot = await gameGateway.GetGameStateAsync(cancellationToken);
        void ReportProgress(string message, GameSnapshot currentSnapshot)
        {
            runLog.AppendProgress(message, currentSnapshot);
            observer.OnProgress(message, currentSnapshot);
        }

        async Task<RunResult> CompleteAsync(RunResult result)
        {
            await runLog.WriteSummaryAsync(result, newRunOptions, cancellationToken);
            return result;
        }

        while (true)
        {
            switch (snapshot.State)
            {
                case "MENU": ReportProgress($"Starting a {newRunOptions.Deck}/{newRunOptions.Stake} run.", snapshot); snapshot = await gameGateway.StartNewRunAsync(newRunOptions, cancellationToken); break;
                case "BLIND_SELECT": ReportProgress("Selecting the current blind.", snapshot); snapshot = await gameGateway.SelectBlindAsync(cancellationToken); break;
                case "SELECTING_HAND":
                    shopDecisions = 0;
                    var hand = await DecideAndExecuteAsync(snapshot, runId, runLog, cancellationToken);
                    if (hand.TerminalResult is not null) return await CompleteAsync(CreateResult(snapshot, runId, startedAt, latencies, jevDecisions, fallbackDecisions, hand.TerminalResult.Value.Outcome, hand.TerminalResult.Value.Detail));
                    snapshot = hand.Snapshot!; Count(hand, latencies, ref jevDecisions, ref fallbackDecisions); break;
                case "SHOP":
                    if (snapshot.DecisionState is null) { ReportProgress("Shop state has no actionable snapshot; leaving the shop.", snapshot); snapshot = await gameGateway.NextRoundAsync(cancellationToken); shopDecisions = 0; break; }
                    if (shopDecisions >= MaxShopDecisions) { ReportProgress("Shop decision limit reached; leaving the shop.", snapshot); snapshot = await gameGateway.NextRoundAsync(cancellationToken); shopDecisions = 0; break; }
                    var shop = await DecideAndExecuteAsync(snapshot, runId, runLog, cancellationToken);
                    if (shop.TerminalResult is not null) { ReportProgress("Shop decision failed; leaving the shop.", snapshot); snapshot = await gameGateway.NextRoundAsync(cancellationToken); shopDecisions = 0; break; }
                    snapshot = shop.Snapshot!; shopDecisions++; Count(shop, latencies, ref jevDecisions, ref fallbackDecisions); break;
                case "SMODS_BOOSTER_OPENED":
                    var pack = await DecideAndExecuteAsync(snapshot, runId, runLog, cancellationToken);
                    if (pack.TerminalResult is not null) return await CompleteAsync(CreateResult(snapshot, runId, startedAt, latencies, jevDecisions, fallbackDecisions, RunOutcome.Aborted, pack.TerminalResult.Value.Detail));
                    snapshot = pack.Snapshot!; Count(pack, latencies, ref jevDecisions, ref fallbackDecisions); break;
                case "ROUND_EVAL": ReportProgress("Blind resolved; cashing out.", snapshot); snapshot = await gameGateway.CashOutAsync(cancellationToken); break;
                case "GAME_OVER": return await CompleteAsync(CreateResult(snapshot, runId, startedAt, latencies, jevDecisions, fallbackDecisions, GetBoolean(snapshot.RawJson, "won") ? RunOutcome.Win : RunOutcome.Loss, "BalatroBot reported GAME_OVER."));
                default: return await CompleteAsync(CreateResult(snapshot, runId, startedAt, latencies, jevDecisions, fallbackDecisions, RunOutcome.Aborted, $"Unsupported BalatroBot state: {snapshot.State}."));
            }
        }
    }

    private async Task<DecisionStepResult> DecideAndExecuteAsync(GameSnapshot snapshot, string runId, RunLog runLog, CancellationToken cancellationToken)
    {
        var state = (snapshot.DecisionState ?? throw new InvalidOperationException($"{snapshot.State} must include a decision state.")) with
        {
            JevBuildIntent = _jevBuildIntent,
            JevRunMemory = _runMemory.ToArray()
        };
        var candidates = actionGenerator.Generate(state);
        if (candidates.Count == 0) return DecisionStepResult.Terminal(RunOutcome.Error, "No legal candidate actions were generated.");
        var stopwatch = Stopwatch.StartNew();
        var plan = new BuildPlanDecision(null, null, null, null);
        var tacticalPlan = new TacticalPlanDecision(null, null, null, null, null, null);
        DecisionResult decision;
        ActionValidationResult validation;
        DecisionSource source;
        try
        {
            plan = await decisionService.PlanAsync(state, cancellationToken);
            if (plan.BuildIntent is null || state.PokerHands.All(hand => hand.Name != plan.BuildIntent))
            {
                throw new InvalidOperationException("Jev did not choose a current poker-hand build.");
            }
            _jevBuildIntent = plan.BuildIntent;
            state = state with { JevBuildIntent = _jevBuildIntent };
            candidates = actionGenerator.Generate(state);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No legal candidate actions were generated after Jev selected a build.");
            }
            if (state.Phase == "SELECTING_HAND")
            {
                try
                {
                    tacticalPlan = await decisionService.PlanTacticsAsync(state, candidates, cancellationToken);
                    if (tacticalPlan.TacticalIntent is not null)
                    {
                        state = state with
                        {
                            JevTacticalIntent = tacticalPlan.TacticalIntent,
                            JevTacticalTargetHand = tacticalPlan.TargetHand,
                            JevExpectedHandsToClear = tacticalPlan.ExpectedHandsToClear
                        };
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    tacticalPlan = new TacticalPlanDecision(null, null, null, exception.Message, null, null);
                }
            }
            decision = await decisionService.DecideAsync(state, candidates, cancellationToken);
            validation = actionValidator.Validate(decision.ActionId, candidates, state);
            source = validation.IsValid ? DecisionSource.Jev : DecisionSource.Fallback;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            decision = new DecisionResult(null, exception.Message, null, null);
            validation = ActionValidationResult.Rejected(exception.Message);
            source = DecisionSource.Fallback;
        }
        var selected = validation.Action ?? _fallbackActionSelector.Select(state, candidates);
        stopwatch.Stop();
        var latest = await gameGateway.GetGameStateAsync(cancellationToken);
        if (latest.State != snapshot.State || latest.DecisionState is null) return DecisionStepResult.Terminal(RunOutcome.Aborted, "The game changed before the selected action could be revalidated.");
        var latestValidation = actionValidator.Validate(selected.Id, candidates, latest.DecisionState);
        if (!latestValidation.IsValid)
        {
            if (snapshot.State == "SHOP") return DecisionStepResult.Terminal(RunOutcome.Aborted, $"The shop action was no longer legal: {latestValidation.Reason}");
            return DecisionStepResult.Terminal(RunOutcome.Aborted, $"The selected action was no longer legal: {latestValidation.Reason}");
        }
        var auditFindings = _decisionAuditor.Audit(state, candidates, selected);
        var record = new DecisionRecord(DateTimeOffset.UtcNow, runId, state.Ante, state.Blind, state, candidates, plan.RawResponse, plan.Confidence, tacticalPlan.RawResponse, tacticalPlan.Confidence, state.JevTacticalTargetHand, state.JevExpectedHandsToClear, decision.RawResponse, decision.Confidence, selected, source, stopwatch.ElapsedMilliseconds, state.Blind.CurrentScore, state.Run.HandsRemaining, state.Run.DiscardsRemaining, auditFindings);
        await runLog.AppendDecisionAsync(record, cancellationToken); observer.OnDecision(record);
        var nextSnapshot = await gameGateway.ExecuteAsync(latestValidation.Action!, cancellationToken);
        Remember(state, selected);
        return DecisionStepResult.Continue(nextSnapshot, source, stopwatch.ElapsedMilliseconds);
    }

    private void Remember(BlindDecisionState state, GameAction selected)
    {
        _runMemory.Add(new RunMemoryEntry(
            _runMemory.Count + 1,
            state.Ante,
            state.Blind.Name,
            state.Blind.CurrentScore,
            state.Blind.RequiredScore,
            state.JevBuildIntent,
            state.JevTacticalIntent,
            state.JevTacticalTargetHand,
            state.JevExpectedHandsToClear,
            selected.Type.ToString(),
            selected.DetectedHand,
            selected.CardIds,
            selected.ProjectedBlindScore));
        if (_runMemory.Count > 12)
        {
            _runMemory.RemoveRange(0, _runMemory.Count - 12);
        }
    }

    private static void Count(DecisionStepResult step, List<long> latencies, ref int jev, ref int fallback) { latencies.Add(step.LatencyMs); if (step.Source == DecisionSource.Jev) jev++; else fallback++; }

    private static RunResult CreateResult(GameSnapshot snapshot, string runId, DateTimeOffset startedAt, IReadOnlyList<long> latencies, int jev, int fallback, RunOutcome outcome, string detail)
    {
        using var document = JsonDocument.Parse(snapshot.RawJson); var root = document.RootElement; var ante = root.TryGetProperty("ante_num", out var anteValue) ? anteValue.GetInt32() : 0; var score = root.TryGetProperty("round", out var round) && round.TryGetProperty("chips", out var chips) ? chips.GetInt32() : 0; var blind = root.TryGetProperty("blinds", out var blinds) ? blinds.EnumerateObject().Select(item => item.Value).FirstOrDefault(item => item.TryGetProperty("status", out var status) && status.GetString() == "CURRENT") : default; var blindName = blind.ValueKind == JsonValueKind.Object && blind.TryGetProperty("name", out var name) ? name.GetString() ?? "UNKNOWN" : "UNKNOWN"; return new RunResult(runId, startedAt, DateTimeOffset.UtcNow, ante, blindName, score, latencies.Count, latencies.Count == 0 ? 0 : latencies.Average(), jev, fallback, outcome, detail);
    }
    private static bool GetBoolean(string json, string property) { using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True; }
    private sealed record DecisionStepResult(GameSnapshot? Snapshot, DecisionSource Source, long LatencyMs, (RunOutcome Outcome, string Detail)? TerminalResult) { public static DecisionStepResult Continue(GameSnapshot snapshot, DecisionSource source, long latencyMs) => new(snapshot, source, latencyMs, null); public static DecisionStepResult Terminal(RunOutcome outcome, string detail) => new(null, DecisionSource.Fallback, 0, (outcome, detail)); }
}

public sealed class DecisionLogWriter(string path)
{
    public RunLog CreateRun(string runId) => new(Path.Combine(Path.GetDirectoryName(path) ?? ".", "runs", runId));
}

public sealed class RunLog
{
    private readonly string _directory;
    private readonly string _decisionPath;
    private readonly string _humanReadablePath;

    public RunLog(string directory)
    {
        _directory = directory;
        _decisionPath = Path.Combine(directory, "decisions.jsonl");
        _humanReadablePath = Path.Combine(directory, "run.log");
        Directory.CreateDirectory(directory);
    }

    public void AppendProgress(string message, GameSnapshot snapshot)
        => File.AppendAllText(_humanReadablePath, $"{DateTimeOffset.UtcNow:O} [GAME] {snapshot.State}: {message}{Environment.NewLine}");

    public async Task AppendDecisionAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        await File.AppendAllTextAsync(_decisionPath, JsonSerializer.Serialize(record) + Environment.NewLine, cancellationToken);
        var audit = record.AuditFindings.Count == 0 ? "none" : string.Join(',', record.AuditFindings.Select(finding => finding.Code));
        File.AppendAllText(_humanReadablePath, $"{record.Timestamp:O} [DECISION] build={record.GameState.JevBuildIntent ?? "none"} tactic={record.GameState.JevTacticalIntent ?? "none"} target={record.JevTacticalTargetHand ?? "none"} expectedHands={record.JevExpectedHandsToClear?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} planConfidence={record.PlanConfidence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} tacticalConfidence={record.TacticalConfidence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} action={record.SelectedAction.Id} actionConfidence={record.ActionConfidence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} source={record.DecisionSource} audit={audit}{Environment.NewLine}");
    }

    public Task WriteSummaryAsync(RunResult result, NewRunOptions options, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(Path.Combine(_directory, "summary.json"), JsonSerializer.Serialize(new RunSummary(options, result)), cancellationToken);

    private sealed record RunSummary(NewRunOptions Options, RunResult Result);
}
