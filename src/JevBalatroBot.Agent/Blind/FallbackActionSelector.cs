using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Agent.Blind;

/// <summary>Chooses a minimal deterministic action only when Jev cannot provide a valid response.</summary>
public sealed class FallbackActionSelector
{
    public GameAction Select(BlindDecisionState state, IReadOnlyList<GameAction> candidates) => state.Phase switch
    {
        "SHOP" => candidates.First(action => action.Type == GameActionType.LeaveShop),
        "SMODS_BOOSTER_OPENED" => candidates.First(action => action.Type == GameActionType.SkipPack),
        _ => candidates
            .Where(action => action.Type == GameActionType.PlayHand)
            .OrderByDescending(action => EstimateScore(state, action))
            .ThenBy(action => action.Id, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? candidates.First()
    };

    private static int EstimateScore(BlindDecisionState state, GameAction action)
    {
        var hand = state.PokerHands.SingleOrDefault(item => item.Name == action.DetectedHand)
            ?? state.PokerHands.SingleOrDefault(item => item.Name == "High Card")
            ?? new PokerHandState("High Card", 1, 5, 1);
        var cardChips = action.CardIndices.Sum(index => RankValue(state.Hand.Cards[index].Rank));
        return (hand.BaseChips + cardChips) * hand.BaseMult;
    }

    private static int RankValue(string rank) => rank switch
    {
        "A" => 11,
        "K" or "Q" or "J" or "T" => 10,
        _ => int.TryParse(rank, out var value) ? value : 0
    };
}
