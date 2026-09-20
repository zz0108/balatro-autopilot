namespace JevBalatroBot.Core.Game;

public sealed record BlindDecisionState(
    int Ante,
    ActiveBlind Blind,
    RunResources Run,
    HandState Hand,
    IReadOnlyList<JokerState> Jokers,
    IReadOnlyList<ConsumableState> Consumables,
    IReadOnlyList<PokerHandState> PokerHands)
{
    public string Phase { get; init; } = string.Empty;
    public int RerollCost { get; init; }
    public CardAreaState Shop { get; init; } = CardAreaState.Empty;
    public int JokerLimit { get; init; }
    public int ConsumableLimit { get; init; }
    public CardAreaState Vouchers { get; init; } = CardAreaState.Empty;
    public CardAreaState Packs { get; init; } = CardAreaState.Empty;
    public CardAreaState OpenPack { get; init; } = CardAreaState.Empty;
    public string? JevBuildIntent { get; init; }
    public string? JevTacticalIntent { get; init; }
    public string? JevTacticalTargetHand { get; init; }
    public int? JevExpectedHandsToClear { get; init; }
    public IReadOnlyList<RunMemoryEntry> JevRunMemory { get; init; } = [];
    public IReadOnlyList<BlindConstraint> BlindConstraints { get; init; } = [];
}

public sealed record ActiveBlind(string Type, string Name, string Effect, int RequiredScore, int CurrentScore, string Key = "");
public sealed record BlindConstraint(string Kind, string Summary, int? Value = null, string? HandType = null, string? Suit = null);

public sealed record RunResources(int Money, int HandsRemaining, int DiscardsRemaining);
public sealed record HandState(int SelectionLimit, IReadOnlyList<PlayingCard> Cards);
public sealed record PlayingCard(int Id, int Index, string Rank, string Suit, string? Enhancement, string? Edition, string? Seal, bool Debuff, bool Hidden, string Label);
public sealed record JokerState(string Name, string Description, string? Edition, int Id = 0, int Index = -1, string Key = "", bool Eternal = false, int SellCost = 0);
public sealed record ConsumableState(string Name, string Description, int Id = 0, int Index = -1, string Key = "", int SellCost = 0);
public sealed record PokerHandState(string Name, int Level, int BaseChips, int BaseMult, int PlayedThisRound = 0);
public sealed record RunMemoryEntry(
    int Turn,
    int Ante,
    string BlindName,
    int ScoreBefore,
    int RequiredScore,
    string? BuildIntent,
    string? TacticalIntent,
    string? TacticalTargetHand,
    int? ExpectedHandsToClear,
    string ActionType,
    string DetectedHand,
    IReadOnlyList<int> SelectedCardIds,
    int? ProjectedBlindScore);
public sealed record CardAreaState(int Limit, IReadOnlyList<MarketCardState> Cards)
{
    public static CardAreaState Empty { get; } = new(0, []);
}

public sealed record MarketCardState(
    int Id,
    int Index,
    string Key,
    string Set,
    string Label,
    string Description,
    int BuyCost,
    int SellCost,
    bool Eternal);
