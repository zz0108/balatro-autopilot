using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class ShopAndPackActionTests
{
    [Fact]
    public void Generate_ShopOffersOnlyAffordablePurchasesAndASafeExit()
    {
        var actions = new CandidateActionGenerator().Generate(CreateState("SHOP"));

        Assert.Contains(actions, action => action.Type == GameActionType.BuyShopCard && action.ItemIndex == 0);
        Assert.DoesNotContain(actions, action => action.Type == GameActionType.BuyShopCard && action.ItemIndex == 1);
        Assert.Contains(actions, action => action.Type == GameActionType.SellJoker && action.ItemIndex == 0);
        Assert.DoesNotContain(actions, action => action.Type == GameActionType.SellJoker && action.ItemIndex == 1);
        Assert.Contains(actions, action => action.Type == GameActionType.LeaveShop);
    }

    [Fact]
    public void Generate_PackAddsLegalTargetSelectionsAndSkip()
    {
        var actions = new CandidateActionGenerator().Generate(CreateState("SMODS_BOOSTER_OPENED"));

        Assert.Contains(actions, action => action.Type == GameActionType.SelectPackCard && action.ItemIndex == 0 && action.CardIndices.Count is >= 1 and <= 2);
        Assert.Contains(actions, action => action.Type == GameActionType.SkipPack);
        Assert.All(actions.GroupBy(action => action.Type), group => Assert.InRange(group.Count(), 1, 32));
    }

    [Fact]
    public void Validate_RejectsShopPurchaseWhenItemIsNoLongerAffordable()
    {
        var state = CreateState("SHOP");
        var candidate = new CandidateActionGenerator().Generate(state).Single(action => action.Type == GameActionType.BuyShopCard && action.ItemIndex == 0);

        var result = new ActionValidator().Validate(candidate.Id, [candidate], state with { Run = state.Run with { Money = 0 } });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsTargetedConsumableDuringHandSelection()
    {
        var state = CreateState("SELECTING_HAND");
        var candidate = new CandidateActionGenerator().Generate(state).First(action => action.Type == GameActionType.UseConsumable);

        var result = new ActionValidator().Validate(candidate.Id, [candidate], state);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Generate_TheHierophantUsesTheGameProvidedKeyAndRequiresOneOrTwoTargets()
    {
        var state = CreateState("SELECTING_HAND") with
        {
            Consumables = [new ConsumableState("The Hierophant", "Enhance up to 2 selected cards.", 201, 0, "c_heirophant", 1)]
        };

        var actions = new CandidateActionGenerator().Generate(state);

        var hierophantActions = actions.Where(action => action.Type == GameActionType.UseConsumable).ToArray();
        Assert.NotEmpty(hierophantActions);
        Assert.All(hierophantActions, action => Assert.InRange(action.CardIndices.Count, 1, 2));
    }

    private static BlindDecisionState CreateState(string phase)
    {
        var hand = new[]
        {
            new PlayingCard(1, 0, "A", "H", null, null, null, false, false, "Ace of Hearts"),
            new PlayingCard(2, 1, "K", "H", null, null, null, false, false, "King of Hearts"),
            new PlayingCard(3, 2, "Q", "H", null, null, null, false, false, "Queen of Hearts")
        };
        return new BlindDecisionState(1, new ActiveBlind("SMALL", "Small Blind", "", 300, 0), new RunResources(4, 4, 3), new HandState(5, hand),
            [new JokerState("Sellable", "", null, 101, 0, "j_sell", false, 2), new JokerState("Eternal", "", null, 102, 1, "j_eternal", true, 2)],
            [new ConsumableState("The Magician", "Lucky", 201, 0, "c_magician", 1)], [])
        {
            Phase = phase,
            RerollCost = 3,
            JokerLimit = 5,
            ConsumableLimit = 2,
            Shop = new CardAreaState(2, [new MarketCardState(301, 0, "j_ok", "JOKER", "Affordable Joker", "+4 Mult", 4, 2, false), new MarketCardState(302, 1, "j_expensive", "JOKER", "Expensive Joker", "", 5, 2, false)]),
            OpenPack = new CardAreaState(2, [new MarketCardState(401, 0, "c_magician", "TAROT", "The Magician", "Lucky", 0, 0, false)])
        };
    }
}
