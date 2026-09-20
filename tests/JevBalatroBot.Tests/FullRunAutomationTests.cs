using System.Text.Json;
using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Balatro.Balatrobot;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class FullRunAutomationTests
{
    [Fact]
    public async Task RunAsync_AutomatesProgressionWhileJevOnlyChoosesCardActions()
    {
        var fixtureJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json"));
        using var document = JsonDocument.Parse(fixtureJson);
        var selectingHand = new GameSnapshot("SELECTING_HAND", fixtureJson, new BalatroStateReducer().Reduce(document.RootElement));
        var gateway = new ScriptedGameGateway(selectingHand);
        var observer = new RecordingObserver();
        var logDirectory = Path.Combine(Path.GetTempPath(), $"jev-balatro-{Guid.NewGuid():N}");
        var logPath = Path.Combine(logDirectory, "decisions.jsonl");
        try
        {
            var runner = new BlindRunner(gateway, new FirstCandidateDecisionService(), new CandidateActionGenerator(), new ActionValidator(), new DecisionLogWriter(logPath), observer);

            var result = await runner.RunAsync(new NewRunOptions("BLUE", "WHITE", null), CancellationToken.None);

            Assert.Equal(RunOutcome.Loss, result.Result);
            Assert.Equal(["start", "select", "play", "cash_out", "next_round"], gateway.Calls);
            Assert.Single(observer.Decisions);
            var runDirectory = Path.Combine(logDirectory, "runs", result.RunId);
            var decisionLine = Assert.Single(File.ReadLines(Path.Combine(runDirectory, "decisions.jsonl")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "run.log")));
            Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
            using var decision = JsonDocument.Parse(decisionLine);
            Assert.Equal("fake-plan", decision.RootElement.GetProperty("JevPlanResponse").GetString());
            Assert.Equal(1, decision.RootElement.GetProperty("PlanConfidence").GetDouble());
            Assert.Equal("fake-tactical", decision.RootElement.GetProperty("JevTacticalResponse").GetString());
            Assert.Equal("score_now", decision.RootElement.GetProperty("GameState").GetProperty("JevTacticalIntent").GetString());
            Assert.False(string.IsNullOrWhiteSpace(decision.RootElement.GetProperty("JevTacticalTargetHand").GetString()));
            Assert.Equal(1, decision.RootElement.GetProperty("JevExpectedHandsToClear").GetInt32());
            Assert.True(decision.RootElement.TryGetProperty("AuditFindings", out _));
            Assert.Equal(1, decision.RootElement.GetProperty("ActionConfidence").GetDouble());
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, true);
        }
    }

    [Fact]
    public async Task RunAsync_ExecutesEveryValidJevChoiceWithoutStrategyOverride()
    {
        var fixtureJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json"));
        using var document = JsonDocument.Parse(fixtureJson);
        var state = new BalatroStateReducer().Reduce(document.RootElement) with
        {
            Blind = new ActiveBlind("BIG", "Big Blind", "", 1_000, 0),
            Run = new RunResources(4, 1, 3)
        };
        var selectingHand = new GameSnapshot("SELECTING_HAND", fixtureJson, state);
        var gateway = new ScriptedGameGateway(selectingHand);
        var observer = new RecordingObserver();
        var logDirectory = Path.Combine(Path.GetTempPath(), $"jev-balatro-{Guid.NewGuid():N}");
        var logPath = Path.Combine(logDirectory, "decisions.jsonl");
        try
        {
            var runner = new BlindRunner(gateway, new HighCardDecisionService(), new CandidateActionGenerator(), new ActionValidator(), new DecisionLogWriter(logPath), observer);

            await runner.RunAsync(new NewRunOptions("BLUE", "WHITE", null), CancellationToken.None);

            Assert.Equal(DecisionSource.Jev, Assert.Single(observer.Decisions).DecisionSource);
            Assert.Equal(GameActionType.PlayHand, gateway.ExecutedAction?.Type);
            Assert.Equal("High Card", gateway.ExecutedAction?.DetectedHand);
        }
        finally
        {
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, true);
        }
    }

    private sealed class FirstCandidateDecisionService : IDecisionService
    {
        public Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken)
            => Task.FromResult(Plan(state));

        public Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
            => Task.FromResult(new TacticalPlanDecision("score_now", state.PokerHands.First().Name, 1, "fake-tactical", 1, "fake-jev"));

        public Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
            => Task.FromResult(new DecisionResult(actions[0].Id, "fake", 1, "fake-jev"));
    }

    private sealed class HighCardDecisionService : IDecisionService
    {
        public Task<BuildPlanDecision> PlanAsync(BlindDecisionState state, CancellationToken cancellationToken)
            => Task.FromResult(Plan(state));

        public Task<TacticalPlanDecision> PlanTacticsAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
            => Task.FromResult(new TacticalPlanDecision("score_now", state.PokerHands.First().Name, 1, "fake-tactical", 1, "fake-jev"));

        public Task<DecisionResult> DecideAsync(BlindDecisionState state, IReadOnlyList<GameAction> actions, CancellationToken cancellationToken)
            => Task.FromResult(new DecisionResult(actions.First(action => action.Type == GameActionType.PlayHand && action.DetectedHand == "High Card").Id, "fake", 1, "fake-jev"));
    }

    private static BuildPlanDecision Plan(BlindDecisionState state)
        => new(state.PokerHands.First().Name, "fake-plan", 1, "fake-jev");

    private sealed class RecordingObserver : IRunObserver
    {
        public List<DecisionRecord> Decisions { get; } = [];
        public void OnProgress(string message, GameSnapshot snapshot) { }
        public void OnDecision(DecisionRecord record) => Decisions.Add(record);
    }

    private sealed class ScriptedGameGateway(GameSnapshot selectingHand) : IGameGateway
    {
        private static readonly GameSnapshot Menu = Snapshot("MENU");
        private static readonly GameSnapshot BlindSelect = Snapshot("BLIND_SELECT");
        private static readonly GameSnapshot RoundEvaluation = Snapshot("ROUND_EVAL", "{\"state\":\"ROUND_EVAL\",\"ante_num\":1,\"round\":{\"chips\":300},\"blinds\":{\"small\":{\"status\":\"CURRENT\",\"name\":\"Small Blind\"}}}");
        private static readonly GameSnapshot Shop = Snapshot("SHOP");
        private static readonly GameSnapshot GameOver = Snapshot("GAME_OVER", "{\"state\":\"GAME_OVER\",\"won\":false,\"ante_num\":1,\"round\":{\"chips\":250},\"blinds\":{}}");

        public List<string> Calls { get; } = [];
        public GameAction? ExecutedAction { get; private set; }
        private int _getCalls;

        public Task<GameSnapshot> GetGameStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(_getCalls++ == 0 ? Menu : selectingHand);

        public Task<GameSnapshot> StartNewRunAsync(NewRunOptions options, CancellationToken cancellationToken) => Call("start", BlindSelect);
        public Task<GameSnapshot> SelectBlindAsync(CancellationToken cancellationToken) => Call("select", selectingHand);
        public Task<GameSnapshot> CashOutAsync(CancellationToken cancellationToken) => Call("cash_out", Shop);
        public Task<GameSnapshot> NextRoundAsync(CancellationToken cancellationToken) => Call("next_round", GameOver);
        public Task<GameSnapshot> ExecuteAsync(GameAction action, CancellationToken cancellationToken)
        {
            ExecutedAction = action;
            return Call("play", RoundEvaluation);
        }

        private Task<GameSnapshot> Call(string name, GameSnapshot snapshot)
        {
            Calls.Add(name);
            return Task.FromResult(snapshot);
        }

        private static GameSnapshot Snapshot(string state, string? json = null) => new(state, json ?? $"{{\"state\":\"{state}\",\"ante_num\":0,\"round\":{{\"chips\":0}},\"blinds\":{{}}}}", null);
    }

}
