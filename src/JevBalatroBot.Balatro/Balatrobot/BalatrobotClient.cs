using System.Text;
using System.Text.Json;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Balatro.Balatrobot;

public sealed class BalatrobotClient(HttpClient httpClient, Uri endpoint, BalatroStateReducer reducer) : IGameGateway
{
    private long _requestId;

    public async Task<GameSnapshot> GetGameStateAsync(CancellationToken cancellationToken)
        => ToSnapshot(await CallAsync("gamestate", null, cancellationToken));

    public async Task<GameSnapshot> StartNewRunAsync(NewRunOptions options, CancellationToken cancellationToken)
        => ToSnapshot(await CallAsync("start", new { deck = options.Deck, stake = options.Stake, seed = options.Seed }, cancellationToken));

    public async Task<GameSnapshot> SelectBlindAsync(CancellationToken cancellationToken)
        => ToSnapshot(await CallAsync("select", null, cancellationToken));

    public async Task<GameSnapshot> CashOutAsync(CancellationToken cancellationToken)
        => ToSnapshot(await CallAsync("cash_out", null, cancellationToken));

    public async Task<GameSnapshot> NextRoundAsync(CancellationToken cancellationToken)
        => ToSnapshot(await CallAsync("next_round", null, cancellationToken));

    public async Task<GameSnapshot> ExecuteAsync(GameAction action, CancellationToken cancellationToken)
    {
        (string method, object parameters) = action.Type switch
        {
            GameActionType.PlayHand => ("play", (object)new { cards = action.CardIndices }),
            GameActionType.Discard => ("discard", (object)new { cards = action.CardIndices }),
            GameActionType.BuyShopCard => ("buy", (object)new { card = action.ItemIndex }),
            GameActionType.BuyVoucher => ("buy", (object)new { voucher = action.ItemIndex }),
            GameActionType.BuyPack => ("buy", (object)new { pack = action.ItemIndex }),
            GameActionType.SellJoker => ("sell", (object)new { joker = action.ItemIndex }),
            GameActionType.SellConsumable => ("sell", (object)new { consumable = action.ItemIndex }),
            GameActionType.RerollShop => ("reroll", (object)new { }),
            GameActionType.LeaveShop => ("next_round", (object)new { }),
            GameActionType.UseConsumable => ("use", (object)new { consumable = action.ItemIndex, cards = action.CardIndices }),
            GameActionType.SelectPackCard => ("pack", (object)new { card = action.ItemIndex, targets = action.CardIndices }),
            GameActionType.SkipPack => ("pack", (object)new { skip = true }),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return ToSnapshot(await CallAsync(method, parameters, cancellationToken));
    }

    private async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters ?? new { }, id = Interlocked.Increment(ref _requestId) });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BalatrobotException($"BalatroBot returned HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new BalatrobotException($"BalatroBot JSON-RPC error: {error.GetRawText()}");
        }

        return document.RootElement.GetProperty("result").Clone();
    }

    private GameSnapshot ToSnapshot(JsonElement raw)
    {
        var rawJson = raw.GetRawText();
        var state = raw.GetProperty("state").GetString() ?? throw new BalatrobotException("BalatroBot state is missing.");
        return state is "SELECTING_HAND" or "SHOP" or "SMODS_BOOSTER_OPENED"
            ? new GameSnapshot(state, rawJson, reducer.Reduce(raw))
            : new GameSnapshot(state, rawJson, null);
    }
}

public sealed class BalatrobotException(string message) : Exception(message);
