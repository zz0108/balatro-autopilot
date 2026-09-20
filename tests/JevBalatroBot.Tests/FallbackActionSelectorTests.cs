using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class FallbackActionSelectorTests
{
    [Fact]
    public void Select_LeavesShopWithoutMakingAStrategicPurchase()
    {
        var state = CreateState("SHOP");
        var buy = new GameAction("buy", GameActionType.BuyShopCard, "", [], [], "") { ItemArea = "shop", ItemId = 1, ItemIndex = 0 };
        var leave = new GameAction("leave", GameActionType.LeaveShop, "", [], [], "");

        var selected = new FallbackActionSelector().Select(state, [buy, leave]);

        Assert.Equal("leave", selected.Id);
    }

    [Fact]
    public void Select_PlaysTheHighestDeterministicBaseScoreWhenJevIsUnavailable()
    {
        var state = CreateState("SELECTING_HAND");
        var highCard = new GameAction("high", GameActionType.PlayHand, "", [0], [1], "High Card");
        var pair = new GameAction("pair", GameActionType.PlayHand, "", [0, 1], [1, 2], "Pair");

        var selected = new FallbackActionSelector().Select(state, [highCard, pair]);

        Assert.Equal("pair", selected.Id);
    }

    private static BlindDecisionState CreateState(string phase) => new(
        1,
        new ActiveBlind("SMALL", "Small Blind", "", 300, 0),
        new RunResources(4, 4, 3),
        new HandState(5,
        [
            new PlayingCard(1, 0, "A", "H", null, null, null, false, false, "Ace of Hearts"),
            new PlayingCard(2, 1, "A", "C", null, null, null, false, false, "Ace of Clubs")
        ]),
        [],
        [],
        [new PokerHandState("High Card", 1, 5, 1), new PokerHandState("Pair", 1, 10, 2)])
    { Phase = phase };
}
