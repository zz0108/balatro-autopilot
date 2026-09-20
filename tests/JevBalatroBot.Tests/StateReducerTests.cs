using System.Text.Json;
using JevBalatroBot.Balatro.Balatrobot;

namespace JevBalatroBot.Tests;

public sealed class StateReducerTests
{
    [Fact]
    public void Reduce_MapsTheRequiredSmallBlindDecisionState()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath));

        var state = new BalatroStateReducer().Reduce(document.RootElement);

        Assert.Equal(1, state.Ante);
        Assert.Equal("SMALL", state.Blind.Type);
        Assert.Equal(300, state.Blind.RequiredScore);
        Assert.Equal(120, state.Blind.CurrentScore);
        Assert.Equal(4, state.Run.HandsRemaining);
        Assert.Equal(3, state.Run.DiscardsRemaining);
        Assert.Equal(8, state.Hand.Cards.Count);
        Assert.Equal("A", state.Hand.Cards[0].Rank);
        Assert.Equal("H", state.Hand.Cards[0].Suit);
        Assert.Equal("Joker", state.Jokers.Single().Name);
        Assert.Contains(state.PokerHands, hand => hand.Name == "Straight Flush" && hand.BaseChips == 100);
    }

    [Fact]
    public void Reduce_MapsStructuredBossConstraintsAndHandUsage()
    {
        const string json = """
            {
              "state":"SELECTING_HAND",
              "round":{"chips":0},
              "blinds":{"boss":{"type":"BOSS","status":"CURRENT","key":"bl_psychic","name":"The Psychic","effect":"","score":600,"constraints":[{"kind":"minimum_played_cards","summary":"Play at least 5 cards.","value":5}]}},
              "hands":{"Pair":{"level":1,"chips":10,"mult":2,"played_this_round":1}}
            }
            """;
        using var document = JsonDocument.Parse(json);

        var state = new BalatroStateReducer().Reduce(document.RootElement);

        Assert.Equal("bl_psychic", state.Blind.Key);
        var constraint = Assert.Single(state.BlindConstraints);
        Assert.Equal("minimum_played_cards", constraint.Kind);
        Assert.Equal(5, constraint.Value);
        Assert.Equal(1, Assert.Single(state.PokerHands).PlayedThisRound);
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json");
}
