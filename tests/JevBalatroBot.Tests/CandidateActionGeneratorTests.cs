using System.Text.Json;
using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Balatro.Balatrobot;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class CandidateActionGeneratorTests
{
    [Fact]
    public void Generate_OnlyUsesCardsInHandAndCreatesUniqueIds()
    {
        var actions = new CandidateActionGenerator().Generate(ReadState());

        Assert.Equal(actions.Count, actions.Select(action => action.Id).Distinct().Count());
        Assert.All(actions, action =>
        {
            Assert.InRange(action.CardIndices.Count, 1, 5);
            Assert.Equal(action.CardIndices.Count, action.CardIndices.Distinct().Count());
            Assert.All(action.CardIndices, index => Assert.InRange(index, 0, 7));
        });
        Assert.Contains(actions, action => action.Type == GameActionType.PlayHand && action.DetectedHand == "Straight Flush");
        Assert.Contains(actions, action => action.Description.Contains("Ace of Hearts", StringComparison.Ordinal));
        Assert.True(actions.Where(action => action.Type == GameActionType.PlayHand).Select(action => action.DetectedHand).Distinct().Count() > 1);
    }

    [Fact]
    public void Generate_OmitsDiscardCandidatesWhenNoDiscardsRemain()
    {
        var state = ReadState() with { Run = ReadState().Run with { DiscardsRemaining = 0 } };

        var actions = new CandidateActionGenerator().Generate(state);

        Assert.DoesNotContain(actions, action => action.Type == GameActionType.Discard);
    }

    [Fact]
    public void Generate_AppliesStructuredHardBlindConstraintsWithoutUsingTheBossName()
    {
        var state = ReadState() with
        {
            Blind = new ActiveBlind("BOSS", "Unrelated Boss", "", 600, 0),
            BlindConstraints =
            [
                new BlindConstraint("minimum_played_cards", "Must play at least 5 cards.", 5),
                new BlindConstraint("single_hand_type_per_round", "Only one hand type may be played this round.")
            ],
            PokerHands = ReadState().PokerHands.Select(hand => hand with { PlayedThisRound = hand.Name == "Pair" ? 1 : 0 }).ToArray()
        };

        var actions = new CandidateActionGenerator().Generate(state);

        Assert.NotEmpty(actions.Where(action => action.Type == GameActionType.PlayHand));
        Assert.All(actions.Where(action => action.Type == GameActionType.PlayHand), action => Assert.Equal("Pair", action.DetectedHand));
        Assert.All(actions.Where(action => action.Type == GameActionType.PlayHand), action => Assert.Equal(5, action.CardIds.Count));
    }

    [Fact]
    public void Generate_ExcludesAlreadyPlayedHandTypesForTheGenericUniqueConstraint()
    {
        var state = ReadState() with
        {
            Blind = new ActiveBlind("BOSS", "Another Unrelated Boss", "", 600, 0),
            BlindConstraints = [new BlindConstraint("unique_hand_type_per_round", "Each hand type may be played only once this round.")],
            PokerHands = ReadState().PokerHands.Select(hand => hand with { PlayedThisRound = hand.Name == "High Card" ? 1 : 0 }).ToArray()
        };

        var actions = new CandidateActionGenerator().Generate(state);

        Assert.DoesNotContain(actions, action => action.Type == GameActionType.PlayHand && action.DetectedHand == "High Card");
        Assert.Contains(actions, action => action.Type == GameActionType.PlayHand && action.DetectedHand == "Pair");
    }

    [Fact]
    public void Generate_DescribesVanillaScorePreviewBlindGapAndBuildAlignment()
    {
        var state = ReadState() with
        {
            JevBuildIntent = "Two Pair",
            Hand = new HandState(5,
            [
                new PlayingCard(1, 0, "4", "S", null, null, null, false, false, "4 of Spades"),
                new PlayingCard(2, 1, "4", "D", null, null, null, false, false, "4 of Diamonds"),
                new PlayingCard(3, 2, "3", "H", null, null, null, false, false, "3 of Hearts"),
                new PlayingCard(4, 3, "3", "D", null, null, null, false, false, "3 of Diamonds")
            ]),
            PokerHands = [new PokerHandState("Two Pair", 1, 20, 2)]
        };

        var action = new CandidateActionGenerator().Generate(state)
            .Single(action => action.Type == GameActionType.PlayHand
                && action.DetectedHand == "Two Pair"
                && action.CardIds.Count == 4);

        Assert.Contains("Base preview: +68", action.Description, StringComparison.Ordinal);
        Assert.Contains("188/300", action.Description, StringComparison.Ordinal);
        Assert.Contains("112 short", action.Description, StringComparison.Ordinal);
        Assert.Contains("Build: MATCH", action.Description, StringComparison.Ordinal);
    }

    private static JevBalatroBot.Core.Game.BlindDecisionState ReadState()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json")));
        return new BalatroStateReducer().Reduce(document.RootElement);
    }
}
