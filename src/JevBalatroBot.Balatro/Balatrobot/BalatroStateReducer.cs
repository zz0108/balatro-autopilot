using System.Text.Json;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Balatro.Balatrobot;

public sealed class BalatroStateReducer
{
    public BlindDecisionState Reduce(JsonElement state)
    {
        var hand = GetArea(state, "hand");
        var cards = hand.Cards.Select(card => new PlayingCard(card.Id, card.Index, GetString(card.Raw, "value", "rank"), GetString(card.Raw, "value", "suit"), GetOptionalString(card.Raw, "modifier", "enhancement"), GetOptionalString(card.Raw, "modifier", "edition"), GetOptionalString(card.Raw, "modifier", "seal"), GetOptionalBoolean(card.Raw, "state", "debuff"), GetOptionalBoolean(card.Raw, "state", "hidden"), card.Label)).ToArray();
        var activeBlind = state.TryGetProperty("blinds", out var blinds) ? blinds.EnumerateObject().Select(property => property.Value).FirstOrDefault(blind => GetString(blind, "status") == "CURRENT") : default;
        var round = TryGetObject(state, "round");
        var jokerArea = GetArea(state, "jokers");
        var consumableArea = GetArea(state, "consumables");

        return new BlindDecisionState(
            GetInt(state, "ante_num"),
            new ActiveBlind(GetString(activeBlind, "type"), GetString(activeBlind, "name"), GetString(activeBlind, "effect"), GetInt(activeBlind, "score"), GetInt(round, "chips"), GetString(activeBlind, "key")),
            new RunResources(GetInt(state, "money"), GetInt(round, "hands_left"), GetInt(round, "discards_left")),
            new HandState(hand.HighlightedLimit, cards),
            jokerArea.Cards.Select(card => new JokerState(card.Label, card.Description, GetOptionalString(card.Raw, "modifier", "edition"), card.Id, card.Index, card.Key, card.Eternal, card.SellCost)).ToArray(),
            consumableArea.Cards.Select(card => new ConsumableState(card.Label, card.Description, card.Id, card.Index, card.Key, card.SellCost)).ToArray(),
            ReducePokerHands(state))
        {
            Phase = GetString(state, "state"),
            RerollCost = GetInt(round, "reroll_cost"),
            JokerLimit = jokerArea.Limit,
            ConsumableLimit = consumableArea.Limit,
            Shop = ToAreaState(GetArea(state, "shop")),
            Vouchers = ToAreaState(GetArea(state, "vouchers")),
            Packs = ToAreaState(GetArea(state, "packs")),
            OpenPack = ToAreaState(GetArea(state, "pack")),
            BlindConstraints = ReduceBlindConstraints(activeBlind)
        };
    }

    private static IReadOnlyList<PokerHandState> ReducePokerHands(JsonElement state)
        => state.TryGetProperty("hands", out var hands) && hands.ValueKind == JsonValueKind.Object ? hands.EnumerateObject().Select(hand => new PokerHandState(hand.Name, GetInt(hand.Value, "level"), GetInt(hand.Value, "chips"), GetInt(hand.Value, "mult"), GetInt(hand.Value, "played_this_round"))).ToArray() : [];

    private static IReadOnlyList<BlindConstraint> ReduceBlindConstraints(JsonElement blind)
        => blind.ValueKind == JsonValueKind.Object && blind.TryGetProperty("constraints", out var constraints) && constraints.ValueKind == JsonValueKind.Array
            ? constraints.EnumerateArray()
                .Where(constraint => constraint.ValueKind == JsonValueKind.Object)
                .Select(constraint => new BlindConstraint(GetString(constraint, "kind"), GetString(constraint, "summary"), GetNullableInt(constraint, "value"), GetOptionalString(constraint, "hand_type"), GetOptionalString(constraint, "suit")))
                .Where(constraint => !string.IsNullOrWhiteSpace(constraint.Kind))
                .ToArray()
            : [];

    private static CardAreaState ToAreaState(Area area) => new(area.Limit, area.Cards.Select(card => new MarketCardState(card.Id, card.Index, card.Key, card.Set, card.Label, card.Description, card.BuyCost, card.SellCost, card.Eternal)).ToArray());

    private static Area GetArea(JsonElement state, string name)
    {
        if (!state.TryGetProperty(name, out var area) || area.ValueKind != JsonValueKind.Object) return Area.Empty;
        var cards = area.TryGetProperty("cards", out var cardArray) && cardArray.ValueKind == JsonValueKind.Array
            ? cardArray.EnumerateArray().Select((card, index) => new ReducedCard(card, index, GetInt(card, "id"), GetString(card, "key"), GetString(card, "set"), GetString(card, "label"), GetString(card, "value", "effect"), GetInt(card, "cost", "buy"), GetInt(card, "cost", "sell"), GetOptionalBoolean(card, "modifier", "eternal"))).ToArray()
            : [];
        return new Area(GetInt(area, "limit"), GetInt(area, "highlighted_limit"), cards);
    }

    private static JsonElement TryGetObject(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static int GetInt(JsonElement element, params string[] properties) { foreach (var property in properties) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out element)) return 0; } return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : 0; }
    private static int? GetNullableInt(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
    private static string GetString(JsonElement element, params string[] properties) { foreach (var property in properties) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out element)) return string.Empty; } return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty; }
    private static string? GetOptionalString(JsonElement element, string container, string property) => element.TryGetProperty(container, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.String ? nested.GetString() : null;
    private static string? GetOptionalString(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool GetOptionalBoolean(JsonElement element, string container, string property) => element.TryGetProperty(container, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.True;
    private sealed record Area(int Limit, int HighlightedLimit, IReadOnlyList<ReducedCard> Cards) { public static Area Empty { get; } = new(0, 0, []); }
    private sealed record ReducedCard(JsonElement Raw, int Index, int Id, string Key, string Set, string Label, string Description, int BuyCost, int SellCost, bool Eternal);
}
