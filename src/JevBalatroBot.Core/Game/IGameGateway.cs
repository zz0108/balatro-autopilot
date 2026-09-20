using JevBalatroBot.Core.Actions;

namespace JevBalatroBot.Core.Game;

public interface IGameGateway
{
    Task<GameSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
    Task<GameSnapshot> StartNewRunAsync(NewRunOptions options, CancellationToken cancellationToken);
    Task<GameSnapshot> SelectBlindAsync(CancellationToken cancellationToken);
    Task<GameSnapshot> CashOutAsync(CancellationToken cancellationToken);
    Task<GameSnapshot> NextRoundAsync(CancellationToken cancellationToken);
    Task<GameSnapshot> ExecuteAsync(GameAction action, CancellationToken cancellationToken);
}

public sealed record GameSnapshot(string State, string RawJson, BlindDecisionState? DecisionState);
public sealed record NewRunOptions(string Deck, string Stake, string? Seed);
