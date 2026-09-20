using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Core.Actions;

public sealed class ActionValidator
{
    public ActionValidationResult Validate(string? selectedActionId, IReadOnlyList<GameAction> candidates, BlindDecisionState state)
    {
        var action = candidates.SingleOrDefault(candidate => candidate.Id == selectedActionId);
        if (action is null)
        {
            return ActionValidationResult.Rejected("The selected action ID is not a current candidate.");
        }

        if (action.Type is GameActionType.LeaveShop or GameActionType.RerollShop or GameActionType.SkipPack)
        {
            return ValidatePhase(action, state);
        }

        if (action.Type is GameActionType.BuyShopCard or GameActionType.BuyVoucher or GameActionType.BuyPack or GameActionType.SellJoker or GameActionType.SellConsumable or GameActionType.UseConsumable or GameActionType.SelectPackCard)
        {
            return ValidateItemAction(action, state);
        }

        if (action.CardIndices.Count == 0 || action.CardIndices.Count > state.Hand.SelectionLimit)
        {
            return ActionValidationResult.Rejected("The selected card count is outside the hand selection limit.");
        }

        if (action.CardIndices.Distinct().Count() != action.CardIndices.Count || action.CardIndices.Any(index => index < 0 || index >= state.Hand.Cards.Count))
        {
            return ActionValidationResult.Rejected("The action contains duplicate or out-of-range card indices.");
        }

        if (action.CardIds.Count != action.CardIndices.Count || action.CardIndices.Where((index, position) => state.Hand.Cards[index].Id != action.CardIds[position]).Any())
        {
            return ActionValidationResult.Rejected("The hand changed after the candidate action was generated.");
        }

        if (action.Type == GameActionType.PlayHand && action.CardIndices.Count is < 1 or > 5)
        {
            return ActionValidationResult.Rejected("A play must select one to five cards.");
        }

        if (action.Type == GameActionType.Discard && state.Run.DiscardsRemaining <= 0)
        {
            return ActionValidationResult.Rejected("No discards remain.");
        }

        return ActionValidationResult.Accepted(action);
    }

    private static ActionValidationResult ValidatePhase(GameAction action, BlindDecisionState state)
    {
        var expected = action.Type switch
        {
            GameActionType.LeaveShop or GameActionType.RerollShop => "SHOP",
            GameActionType.SkipPack => "SMODS_BOOSTER_OPENED",
            _ => string.Empty
        };
        return state.Phase == expected
            ? ActionValidationResult.Accepted(action)
            : ActionValidationResult.Rejected($"The action requires {expected}, but the game is in {state.Phase}.");
    }

    private static ActionValidationResult ValidateItemAction(GameAction action, BlindDecisionState state)
    {
        var area = action.ItemArea switch
        {
            "shop" => state.Shop.Cards,
            "vouchers" => state.Vouchers.Cards,
            "packs" => state.Packs.Cards,
            "jokers" => state.Jokers.Select(joker => new MarketCardState(joker.Id, joker.Index, joker.Key, "JOKER", joker.Name, joker.Description, 0, joker.SellCost, joker.Eternal)).ToArray(),
            "consumables" => state.Consumables.Select(consumable => new MarketCardState(consumable.Id, consumable.Index, consumable.Key, "CONSUMABLE", consumable.Name, consumable.Description, 0, consumable.SellCost, false)).ToArray(),
            "pack" => state.OpenPack.Cards,
            _ => []
        };

        var item = area.SingleOrDefault(card => card.Index == action.ItemIndex && card.Id == action.ItemId);
        if (item is null)
        {
            return ActionValidationResult.Rejected("The selected item is no longer present in its original area.");
        }

        var validPhase = action.Type switch
        {
            GameActionType.SelectPackCard => state.Phase == "SMODS_BOOSTER_OPENED",
            GameActionType.UseConsumable => state.Phase is "SELECTING_HAND" or "SHOP",
            _ => state.Phase == "SHOP"
        };
        if (!validPhase)
        {
            return ActionValidationResult.Rejected($"The action is not available while the game is in {state.Phase}.");
        }

        if ((action.Type is GameActionType.BuyShopCard or GameActionType.BuyVoucher or GameActionType.BuyPack) && item.BuyCost > state.Run.Money)
        {
            return ActionValidationResult.Rejected("The selected item is no longer affordable.");
        }

        if (action.Type == GameActionType.BuyShopCard && item.Set == "JOKER" && state.Jokers.Count >= state.JokerLimit)
        {
            return ActionValidationResult.Rejected("Joker slots are full.");
        }

        if (action.Type == GameActionType.BuyShopCard && (item.Set is "PLANET" or "SPECTRAL" or "TAROT") && state.Consumables.Count >= state.ConsumableLimit)
        {
            return ActionValidationResult.Rejected("Consumable slots are full.");
        }

        if (action.CardIndices.Count > 0 && !ValidateTargetCards(action, state, out var reason))
        {
            return ActionValidationResult.Rejected(reason);
        }

        return ActionValidationResult.Accepted(action);
    }

    private static bool ValidateTargetCards(GameAction action, BlindDecisionState state, out string reason)
    {
        reason = string.Empty;
        if (action.CardIndices.Distinct().Count() != action.CardIndices.Count || action.CardIndices.Any(index => index < 0 || index >= state.Hand.Cards.Count))
        {
            reason = "The action contains duplicate or out-of-range target card indices.";
            return false;
        }

        if (action.CardIds.Count != action.CardIndices.Count || action.CardIndices.Where((index, position) => state.Hand.Cards[index].Id != action.CardIds[position]).Any())
        {
            reason = "The target cards changed after the candidate action was generated.";
            return false;
        }

        return true;
    }
}

public sealed record ActionValidationResult(bool IsValid, GameAction? Action, string? Reason)
{
    public static ActionValidationResult Accepted(GameAction action) => new(true, action, null);
    public static ActionValidationResult Rejected(string reason) => new(false, null, reason);
}
