using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Agent.Blind;

public sealed class CandidateActionGenerator
{
    private const int MaxCandidatesPerType = 32;

    public IReadOnlyList<GameAction> Generate(BlindDecisionState state)
    {
        var actions = state.Phase switch
        {
            "SELECTING_HAND" => GenerateHand(state),
            "SHOP" => GenerateShop(state),
            "SMODS_BOOSTER_OPENED" => GeneratePack(state),
            _ => []
        };
        return ApplyHardBlindConstraints(actions, state)
            .GroupBy(action => action.Type)
            .SelectMany(group => SelectDiverse(group))
            .OrderBy(action => action.Type)
            .ThenBy(action => action.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<GameAction> ApplyHardBlindConstraints(IEnumerable<GameAction> actions, BlindDecisionState state)
    {
        var playActions = actions.Where(action => action.Type == GameActionType.PlayHand);
        foreach (var constraint in state.BlindConstraints)
        {
            playActions = constraint.Kind switch
            {
                "minimum_played_cards" when constraint.Value is { } minimum => playActions.Where(action => action.CardIds.Count >= minimum),
                "single_hand_type_per_round" => KeepSingleHandType(playActions, state.PokerHands),
                "unique_hand_type_per_round" => playActions.Where(action => !state.PokerHands.Any(hand => hand.Name == action.DetectedHand && hand.PlayedThisRound > 0)),
                _ => playActions
            };
        }

        var permittedPlayIds = playActions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        return actions.Where(action => action.Type != GameActionType.PlayHand || permittedPlayIds.Contains(action.Id));
    }

    private static IEnumerable<GameAction> KeepSingleHandType(IEnumerable<GameAction> actions, IReadOnlyList<PokerHandState> pokerHands)
    {
        var usedHandTypes = pokerHands.Where(hand => hand.PlayedThisRound > 0).Select(hand => hand.Name).Distinct(StringComparer.Ordinal).ToArray();
        return usedHandTypes.Length == 1 ? actions.Where(action => action.DetectedHand == usedHandTypes[0]) : actions;
    }

    private static IReadOnlyList<GameAction> SelectDiverse(IEnumerable<GameAction> actions)
    {
        var groups = actions
            .GroupBy(DiversityKey)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Queue<GameAction>(group.OrderBy(action => action.Id, StringComparer.Ordinal)))
            .ToArray();
        var selected = new List<GameAction>(MaxCandidatesPerType);
        while (selected.Count < MaxCandidatesPerType && groups.Any(group => group.Count > 0))
        {
            foreach (var group in groups)
            {
                if (selected.Count == MaxCandidatesPerType) break;
                if (group.Count > 0) selected.Add(group.Dequeue());
            }
        }
        return selected;
    }

    private static string DiversityKey(GameAction action) => action.Type switch
    {
        GameActionType.PlayHand => $"{action.Type}:{action.DetectedHand}",
        GameActionType.Discard => $"{action.Type}:{action.CardIds.Count}",
        _ when action.CardIds.Count > 0 => $"{action.Type}:{action.ItemArea}:{action.ItemId}:{action.CardIds.Count}",
        _ => $"{action.Type}:{action.ItemArea}:{action.ItemId}"
    };

    private static IReadOnlyList<GameAction> GenerateHand(BlindDecisionState state)
    {
        var candidates = new List<GameAction>();
        foreach (var selection in Combinations(state.Hand.Cards, Math.Min(5, state.Hand.SelectionLimit)))
        {
            var hand = Classify(selection);
            var preview = DescribePlay(state, selection, hand);
            Add(candidates, GameActionType.PlayHand, preview.Description, selection.Select(card => card.Index).ToArray(), selection.Select(card => card.Id).ToArray(), hand, preview: preview);
        }

        if (state.Run.DiscardsRemaining > 0)
        {
            foreach (var selection in TargetCombinations(state.Hand.Cards, 1, Math.Min(state.Hand.SelectionLimit, state.Hand.Cards.Count))) Add(candidates, GameActionType.Discard, $"Discard: {string.Join(", ", selection.Select(DescribeCard))}. No immediate blind score; redraw for a later hand. {DescribeRoundPosition(state)}", selection.Select(card => card.Index).ToArray(), selection.Select(card => card.Id).ToArray(), "Discard");
        }

        foreach (var consumable in state.Consumables)
        {
            AddConsumableActions(candidates, state, consumable, GameActionType.UseConsumable, "Use", "consumables");
        }
        return candidates;
    }

    private static IReadOnlyList<GameAction> GenerateShop(BlindDecisionState state)
    {
        var candidates = new List<GameAction>();
        foreach (var card in state.Shop.Cards.Where(card => card.BuyCost <= state.Run.Money && HasPurchaseSlot(card, state))) AddItem(candidates, GameActionType.BuyShopCard, $"Buy {card.Label} for ${card.BuyCost}: {card.Description}", "shop", card);
        foreach (var card in state.Vouchers.Cards.Where(card => card.BuyCost <= state.Run.Money)) AddItem(candidates, GameActionType.BuyVoucher, $"Redeem voucher {card.Label} for ${card.BuyCost}: {card.Description}", "vouchers", card);
        foreach (var card in state.Packs.Cards.Where(card => card.BuyCost <= state.Run.Money)) AddItem(candidates, GameActionType.BuyPack, $"Buy booster {card.Label} for ${card.BuyCost}: {card.Description}", "packs", card);
        foreach (var joker in state.Jokers.Where(joker => !joker.Eternal)) Add(candidates, GameActionType.SellJoker, $"Sell Joker {joker.Name} for ${joker.SellCost}: {joker.Description}", [], [], "", "jokers", joker.Index, joker.Id);
        foreach (var consumable in state.Consumables) Add(candidates, GameActionType.SellConsumable, $"Sell consumable {consumable.Name} for ${consumable.SellCost}: {consumable.Description}", [], [], "", "consumables", consumable.Index, consumable.Id);
        foreach (var consumable in state.Consumables.Where(consumable => !TargetRequirements.RequiresCards(consumable.Key))) AddConsumableActions(candidates, state, consumable, GameActionType.UseConsumable, "Use", "consumables");
        if (state.RerollCost <= state.Run.Money) Add(candidates, GameActionType.RerollShop, $"Reroll shop for ${state.RerollCost}.", [], [], "");
        Add(candidates, GameActionType.LeaveShop, "Leave the shop and proceed to the next blind.", [], [], "");
        return candidates;
    }

    private static IReadOnlyList<GameAction> GeneratePack(BlindDecisionState state)
    {
        var candidates = new List<GameAction>();
        foreach (var card in state.OpenPack.Cards)
        {
            if (card.Set == "JOKER" && state.Jokers.Count >= state.JokerLimit) continue;
            if (TargetRequirements.RequiresJoker(card.Key) && state.Jokers.Count == 0) continue;
            AddPackActions(candidates, state, card);
        }
        Add(candidates, GameActionType.SkipPack, "Skip the remaining booster-pack choices.", [], [], "");
        return candidates;
    }

    private static void AddPackActions(List<GameAction> candidates, BlindDecisionState state, MarketCardState card)
    {
        var requirement = TargetRequirements.For(card.Key);
        if (requirement is null)
        {
            AddItem(candidates, GameActionType.SelectPackCard, $"Select pack card {card.Label}: {card.Description}", "pack", card);
            return;
        }
        foreach (var targets in TargetCombinations(state.Hand.Cards, requirement.Value.Min, requirement.Value.Max)) Add(candidates, GameActionType.SelectPackCard, $"Select pack card {card.Label} targeting {string.Join(", ", targets.Select(DescribeCard))}: {card.Description}", targets.Select(card => card.Index).ToArray(), targets.Select(card => card.Id).ToArray(), "", "pack", card.Index, card.Id);
    }

    private static void AddConsumableActions(List<GameAction> candidates, BlindDecisionState state, ConsumableState consumable, GameActionType type, string verb, string area)
    {
        var requirement = TargetRequirements.For(consumable.Key);
        var tacticalContext = state.Phase == "SELECTING_HAND"
            ? $" No immediate blind score; use targets to advance the current build ({state.JevBuildIntent ?? "none"}) or a later hand. {DescribeRoundPosition(state)}"
            : string.Empty;
        if (requirement is null)
        {
            Add(candidates, type, $"{verb} consumable {consumable.Name}: {consumable.Description}.{tacticalContext}", [], [], "", area, consumable.Index, consumable.Id);
            return;
        }
        foreach (var targets in TargetCombinations(state.Hand.Cards, requirement.Value.Min, requirement.Value.Max)) Add(candidates, type, $"{verb} consumable {consumable.Name} on {string.Join(", ", targets.Select(DescribeCard))}: {consumable.Description}.{tacticalContext}", targets.Select(card => card.Index).ToArray(), targets.Select(card => card.Id).ToArray(), "", area, consumable.Index, consumable.Id);
    }

    private static bool HasPurchaseSlot(MarketCardState card, BlindDecisionState state) => card.Set switch { "JOKER" => state.Jokers.Count < state.JokerLimit, "PLANET" or "SPECTRAL" or "TAROT" => state.Consumables.Count < state.ConsumableLimit, _ => true };
    private static void AddItem(List<GameAction> candidates, GameActionType type, string description, string area, MarketCardState card) => Add(candidates, type, description, [], [], "", area, card.Index, card.Id);
    private static void Add(List<GameAction> candidates, GameActionType type, string description, IReadOnlyList<int> indices, IReadOnlyList<int> cardIds, string detectedHand, string? area = null, int? itemIndex = null, int? itemId = null, PlayPreview? preview = null)
    {
        candidates.Add(new GameAction($"action_{candidates.Count + 1:000}", type, description, indices, cardIds, detectedHand)
        {
            ItemArea = area,
            ItemIndex = itemIndex,
            ItemId = itemId,
            BaseScorePreview = preview?.BaseScore,
            ProjectedBlindScore = preview?.ProjectedScore,
            RemainingBlindGap = preview?.RemainingGap
        });
    }

    private static PlayPreview DescribePlay(BlindDecisionState state, IReadOnlyList<PlayingCard> selection, string handName)
    {
        var baseScore = EstimateVanillaBaseScore(selection, handName, state.PokerHands);
        var projectedScore = state.Blind.CurrentScore + baseScore;
        var gap = Math.Max(0, state.Blind.RequiredScore - projectedScore);
        var buildAlignment = string.IsNullOrWhiteSpace(state.JevBuildIntent)
            ? "NONE"
            : string.Equals(handName, state.JevBuildIntent, StringComparison.Ordinal) ? "MATCH" : "MISMATCH";
        return new PlayPreview($"Play: {string.Join(", ", selection.Select(DescribeCard))}. Detected hand: {handName}. Base preview: +{baseScore} (no Jokers/card effects) -> {projectedScore}/{state.Blind.RequiredScore}; {(gap == 0 ? "blind cleared" : $"{gap} short")}; {Math.Max(0, state.Run.HandsRemaining - 1)} hands after. Build: {buildAlignment}.", baseScore, projectedScore, gap);
    }

    private static string DescribeRoundPosition(BlindDecisionState state)
        => $"Score {state.Blind.CurrentScore}/{state.Blind.RequiredScore}; {state.Run.HandsRemaining} hands and {state.Run.DiscardsRemaining} discards remain.";

    private static int EstimateVanillaBaseScore(IReadOnlyList<PlayingCard> selection, string handName, IReadOnlyList<PokerHandState> pokerHands)
    {
        var hand = pokerHands.SingleOrDefault(item => item.Name == handName)
            ?? pokerHands.SingleOrDefault(item => item.Name == "High Card")
            ?? new PokerHandState("High Card", 1, 5, 1);
        var cardChips = GetVanillaScoringCards(selection, handName).Sum(card => RankChipValue(card.Rank));
        return (hand.BaseChips + cardChips) * hand.BaseMult;
    }

    private static IReadOnlyList<PlayingCard> GetVanillaScoringCards(IReadOnlyList<PlayingCard> cards, string handName)
    {
        if (handName is "Straight Flush" or "Flush" or "Straight" or "Full House") return cards;
        var groups = cards.GroupBy(card => card.Rank).OrderByDescending(group => RankChipValue(group.Key)).ToArray();
        return handName switch
        {
            "Four of a Kind" => groups.First(group => group.Count() == 4).ToArray(),
            "Three of a Kind" => groups.First(group => group.Count() == 3).ToArray(),
            "Two Pair" => groups.Where(group => group.Count() >= 2).Take(2).SelectMany(group => group.Take(2)).ToArray(),
            "Pair" => groups.First(group => group.Count() >= 2).Take(2).ToArray(),
            _ => [cards.OrderByDescending(card => RankChipValue(card.Rank)).First()]
        };
    }

    private static int RankChipValue(string rank) => rank switch
    {
        "A" => 11,
        "K" or "Q" or "J" or "T" => 10,
        _ when int.TryParse(rank, out var value) => value,
        _ => 0
    };

    private sealed record PlayPreview(string Description, int BaseScore, int ProjectedScore, int RemainingGap);

    private static IEnumerable<IReadOnlyList<PlayingCard>> TargetCombinations(IReadOnlyList<PlayingCard> cards, int min, int max)
    {
        for (var length = min; length <= Math.Min(max, cards.Count); length++) foreach (var selection in CombinationsOfLength(cards, length, 0, [])) yield return selection;
    }
    private static IEnumerable<IReadOnlyList<PlayingCard>> Combinations(IReadOnlyList<PlayingCard> cards, int maximum) { for (var length = 1; length <= maximum; length++) foreach (var selection in CombinationsOfLength(cards, length, 0, [])) yield return selection; }
    private static IEnumerable<IReadOnlyList<PlayingCard>> CombinationsOfLength(IReadOnlyList<PlayingCard> cards, int remaining, int start, List<PlayingCard> chosen) { if (remaining == 0) { yield return chosen.ToArray(); yield break; } for (var index = start; index <= cards.Count - remaining; index++) { chosen.Add(cards[index]); foreach (var combination in CombinationsOfLength(cards, remaining - 1, index + 1, chosen)) yield return combination; chosen.RemoveAt(chosen.Count - 1); } }
    private static string DescribeCard(PlayingCard card) { var rank = card.Rank switch { "T" => "10", "J" => "Jack", "Q" => "Queen", "K" => "King", "A" => "Ace", _ => card.Rank }; var suit = card.Suit switch { "H" => "Hearts", "D" => "Diamonds", "C" => "Clubs", "S" => "Spades", _ => card.Suit }; return string.IsNullOrWhiteSpace(card.Enhancement) ? $"{rank} of {suit}" : $"{card.Enhancement} {rank} of {suit}"; }
    private static string Classify(IReadOnlyList<PlayingCard> cards) { var rankCounts = cards.GroupBy(card => card.Rank).Select(group => group.Count()).OrderDescending().ToArray(); var flush = cards.Count >= 5 && cards.Select(card => card.Suit).Distinct().Count() == 1; var straight = cards.Count >= 5 && IsStraight(cards.Select(card => card.Rank)); if (flush && straight) return "Straight Flush"; if (rankCounts[0] == 4) return "Four of a Kind"; if (rankCounts[0] == 3 && rankCounts.Length > 1 && rankCounts[1] == 2) return "Full House"; if (flush) return "Flush"; if (straight) return "Straight"; if (rankCounts[0] == 3) return "Three of a Kind"; if (rankCounts.Count(count => count == 2) >= 2) return "Two Pair"; if (rankCounts[0] == 2) return "Pair"; return "High Card"; }
    private static bool IsStraight(IEnumerable<string> ranks) { var order = new[] { "2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A" }; var values = ranks.Select(rank => Array.IndexOf(order, rank)).Distinct().Order().ToArray(); return values.Length == 5 && (values[^1] - values[0] == 4 || values.SequenceEqual([0, 1, 2, 3, 12])); }

    private static class TargetRequirements
    {
        private static readonly IReadOnlyDictionary<string, (int Min, int Max)> Rules = new Dictionary<string, (int, int)>(StringComparer.Ordinal) { ["c_magician"] = (1, 2), ["c_empress"] = (1, 2), ["c_heirophant"] = (1, 2), ["c_strength"] = (1, 2), ["c_hanged_man"] = (1, 2), ["c_death"] = (2, 2), ["c_lovers"] = (1, 1), ["c_chariot"] = (1, 1), ["c_justice"] = (1, 1), ["c_devil"] = (1, 1), ["c_tower"] = (1, 1), ["c_aura"] = (1, 1), ["c_star"] = (1, 3), ["c_moon"] = (1, 3), ["c_sun"] = (1, 3), ["c_world"] = (1, 3) };
        public static (int Min, int Max)? For(string key) => Rules.TryGetValue(key, out var value) ? value : null;
        public static bool RequiresCards(string key) => For(key) is not null;
        public static bool RequiresJoker(string key) => key == "c_ankh";
    }
}
