namespace JevBalatroBot.Core.Actions;

public enum GameActionType
{
    PlayHand,
    Discard,
    BuyShopCard,
    BuyVoucher,
    BuyPack,
    SellJoker,
    SellConsumable,
    RerollShop,
    LeaveShop,
    UseConsumable,
    SelectPackCard,
    SkipPack
}

public sealed record GameAction(
    string Id,
    GameActionType Type,
    string Description,
    IReadOnlyList<int> CardIndices,
    IReadOnlyList<int> CardIds,
    string DetectedHand)
{
    public string? ItemArea { get; init; }
    public int? ItemIndex { get; init; }
    public int? ItemId { get; init; }
    public int? BaseScorePreview { get; init; }
    public int? ProjectedBlindScore { get; init; }
    public int? RemainingBlindGap { get; init; }
}
