# Upstream integration research

Researched 2026-09-19. This note uses upstream repositories and vendor documentation only. It records the contract to implement against, not an assertion that the local game or an API key has been integration-tested.

## Recommendation

Use [BalatroBot](https://github.com/coder/balatrobot) directly for the first PoC, with a small JSON-RPC client. It is the actively released integration that exposes the real running game over localhost and has a published OpenRPC contract. Its current upstream release is [v1.5.2](https://github.com/coder/balatrobot/releases/tag/v1.5.2); the bundled mod manifest is also version `1.5.2` and declares `Steamodded (>=1.~)` as its dependency ([manifest](https://raw.githubusercontent.com/coder/balatrobot/main/balatrobot.json)).

This preserves the intended boundary: the C# application obtains state, constructs a **closed** list of candidate `PlayHand` and `Discard` actions, has Jev choose one candidate ID, validates it again, and only then emits the corresponding BalatroBot request. Jev must never receive a free-form game command.

Do **not** use Balatro MCP as the runtime for milestone 1. It is useful protocol evidence because it can enumerate action names, but it introduces MCP even though the requested first version explicitly excludes it. See [Alternatives investigated](#alternatives-investigated).

## Mod-loading chain

BalatroBot's upstream installation guide specifies Balatro `v1.0.1+`, Lovely `v0.8.0+`, Steamodded `v1.0.0-beta-1221a+`, and uv `v0.9.21+`; it tells Windows users to place the mod under `%AppData%/Balatro/Mods/balatrobot/` and launch `uvx balatrobot serve` ([installation guide](https://coder.github.io/balatrobot/latest/installation/)). The guide's verification request is a localhost `health` JSON-RPC call.

[Lovely](https://github.com/ethangreen-dev/lovely-injector) is the runtime Lua injector. Its [Windows installation instructions](https://raw.githubusercontent.com/ethangreen-dev/lovely-injector/master/README.md) place `version.dll` beside `Balatro.exe` and mods under `%AppData%/Balatro/Mods`; its most recent upstream release is [v0.9.0](https://github.com/ethangreen-dev/lovely-injector/releases/tag/v0.9.0). [Steamodded](https://github.com/Steamodded/smods) is the Lua Balatro mod loader/framework; its upstream README says a mod file or folder belongs in the Mods directory ([README](https://raw.githubusercontent.com/Steamodded/smods/main/README.md)). Its most recent GitHub release is [26.829.0](https://github.com/Steamodded/smods/releases/tag/26.829.0).

**UNKNOWN:** no upstream source found that pins a current Steam Balatro build to a specific Lovely/Steamodded/BalatroBot compatibility matrix. The documented `v1.0.1+` is a minimum, not proof of compatibility with the locally installed game. Before any end-to-end claim, start the installed game, call `health`, and record `rpc.discover` from that exact running instance.

## BalatroBot protocol

The [upstream API reference](https://coder.github.io/balatrobot/latest/api/) defines JSON-RPC 2.0 over HTTP/1.1 at default endpoint `http://127.0.0.1:12346`, with `Content-Type: application/json`.

```json
{
  "jsonrpc": "2.0",
  "method": "gamestate",
  "params": {},
  "id": 1
}
```

Successful responses return `{ "jsonrpc": "2.0", "result": { ... }, "id": 1 }`; errors return an `error` object with `code`, `message`, and error `data` ([response format](https://coder.github.io/balatrobot/latest/api/#response-format)). The `rpc.discover` method returns the server's OpenRPC specification, so it is the required runtime contract-discovery step rather than a schema reconstructed from this note ([API reference](https://coder.github.io/balatrobot/latest/api/#rpcdiscover)).

### Required first-blind operations

| Purpose | JSON-RPC method and parameters | Preconditions / upstream contract |
| --- | --- | --- |
| Check connection | `health` | Returns `{ "status": "ok" }`. |
| Read state | `gamestate` | Returns the complete `GameState`. |
| Start a run (optional) | `start`, with `deck`, `stake`, optional `seed` | Requires `MENU`; returns `BLIND_SELECT`. |
| Enter Small Blind | `select` | Requires `BLIND_SELECT`; returns `SELECTING_HAND`. |
| Play a candidate | `play`, `{ "cards": [0, 2, 4] }` | Requires `SELECTING_HAND`; `cards` is a **0-based** integer array of **1–5** hand indices. |
| Discard a candidate | `discard`, `{ "cards": [0, 1] }` | Requires `SELECTING_HAND`; `cards` is a **0-based** integer array. |

The exact operations and preconditions are documented by the upstream [quick start](https://coder.github.io/balatrobot/latest/api/#quickstart), [`play`](https://coder.github.io/balatrobot/latest/api/#play), and [`discard`](https://coder.github.io/balatrobot/latest/api/#discard) entries.

BalatroBot also publishes operations outside scope for milestone 1: `skip`, `buy`, `pack`, `sell`, `reroll`, `cash_out`, `next_round`, `rearrange`, `sort`, `use`, `add`, `screenshot`, and `set` ([method list](https://coder.github.io/balatrobot/latest/api/#methods)). Do not invoke these from the first-blind agent. In particular, `add` and `set` are documented as debug/testing operations.

### State and action legality

The published `GameState` schema includes the required fields `state`, `round_num`, `ante_num`, and `money`, and can include `hands`, `round`, `blinds`, `jokers`, `consumables`, and `hand` ([GameState schema](https://coder.github.io/balatrobot/latest/api/#gamestate-schema)). The upstream OpenRPC document in the source tree identifies the needed detailed fields:

- `round.hands_left`, `round.discards_left`, and `round.chips`;
- `hand.count`, `hand.limit`, `hand.highlighted_limit`, and `hand.cards`;
- each card's `id`, `key`, `set`, `label`, `value.{suit,rank,effect}`, `modifier.{seal,edition,enhancement}`, and `state.debuff`;
- each poker hand's `level`, `chips`, `mult`, `played`, and `played_this_round`.

Source: [BalatroBot OpenRPC schema at `main`](https://raw.githubusercontent.com/coder/balatrobot/main/src/lua/utils/openrpc.json). Its `info.version` is `1.5.2`, so fixture deserialization should tolerate unknown/new fields and store the captured discovery document with integration-test artefacts.

**No BalatroBot `legal_actions`/`available_actions` endpoint was found in the upstream method list or OpenRPC schema.** The application therefore owns candidate generation and validation:

1. only generate candidates when the just-read state is `SELECTING_HAND`;
2. use positions within the current `hand.cards` array; reject duplicates and any Play set outside 1–5 cards;
3. omit discards when `round.discards_left <= 0`;
4. immediately before execution, read state again and validate the chosen candidate against that new state; and
5. send only `play` or `discard` with validated 0-based indices.

This is a design conclusion from the published API, not an API feature. It is also the necessary safety boundary for the stated PoC.

### Version ambiguity to avoid

The human-readable API docs show `start` examples using values such as `b_red` and `stake_white`, while the OpenRPC source tree represents deck/stake enumerations differently. **Do not hard-code a startup payload based only on this note.** If automated run creation is retained, obtain the runtime `rpc.discover` document and prove one `start` call in integration testing. The interactive use case can start the run manually and only begin at `SELECTING_HAND`.

## Jev / TypeSafe API

The official vendor is [TypeSafe AI](https://typesafe.ai/). Its [official API reference](https://docs.typesafe.ai/api) defines:

```http
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <API_KEY>
Content-Type: application/json
```

The request requires `model`, `state`, and a caller-keyed `questions` map. `state` may be a string, object, or array; model `jev-latest` is the documented flagship alias. For the agent, send a compact `BlindDecisionState` plus action descriptions, and ask one `choice` question whose criteria keys are candidate action IDs:

```json
{
  "model": "jev-1.13.0",
  "state": {
    "blind": { "requiredScore": 300, "currentScore": 120 },
    "actions": ["action_001: Play ...", "action_002: Discard ..."]
  },
  "questions": {
    "selected_action": {
      "type": "choice",
      "instructions": "Choose exactly one provided legal action to win the current blind.",
      "criteria": {
        "action_001": "Play the described cards.",
        "action_002": "Discard the described cards."
      }
    }
  }
}
```

The documented Choice response is `answers.selected_action.{type,choice,probabilities,confidence}`; root fields include resolved `model` and `usage.{input_tokens,output_tokens}` ([API reference](https://docs.typesafe.ai/api#choice-answer)). The returned `choice` must still pass the application's candidate-ID and re-legality checks.

The official docs define three primitives: Choice, Score, and Noul ([introduction](https://docs.typesafe.ai/introduction)). Choice is the correct primitive for this PoC because it selects exactly one member of a closed set. Noul is a 0–1 yes probability and Score is a probability-weighted position on an ordered rubric; neither should be used as an executable action command.

The [models page](https://docs.typesafe.ai/models) currently documents Jev `1.13.0`, alias `jev-latest`, $0.042/M input tokens, a 64k-token total request budget, 32k token state-plus-longest-question budget, and dynamic limits of 250,000 tokens/second and 1,200 requests/minute. Pin `jev-1.13.0` for reproducible experiment records and log the server-returned model identifier. The vendor's API errors are 401, 422, 429, and 529; 429/529 should use exponential backoff ([errors](https://docs.typesafe.ai/api#errors)).

The official [SDK page](https://docs.typesafe.ai/sdk) lists Python and JavaScript SDKs. **UNKNOWN:** no official .NET/C# SDK was found in the official SDK index. A thin `HttpClient` + `System.Text.Json` adapter is therefore the appropriate dependency-free .NET implementation.

**UNKNOWN:** this repository has no TypeSafe API key, so account access, live model availability, actual rate limits, latency, and request/response integration have not been tested.

## Alternatives investigated

### Evalatro

[Evalatro](https://github.com/alesha-pro/evalatro) is an upstream open benchmark, not a required library. It uses the real Balatro executable through BalatroBot, records decisions/states/tokens/outcomes, and has a `npm run live -- naive` smoke test that exercises Balatro, Lovely, Steamodded, BalatroBot, and the runner ([README](https://raw.githubusercontent.com/alesha-pro/evalatro/master/README.md)). It is useful reference material for setup, decision logging, and truthful E2E status reporting, but importing it would add a Node/TypeScript runtime outside the requested .NET console PoC.

### Balatro MCP

[Gdnaiteab/Balatro-MCP](https://github.com/Gdnaiteab/Balatro-MCP) is a Steamodded mod plus Python MCP wrapper. Its local API is `GET /health`, `GET /state`, `GET /actions/available`, and `POST /action` at `http://127.0.0.1:8080` ([README](https://raw.githubusercontent.com/Gdnaiteab/Balatro-MCP/main/README.md)). Its source advertises action names such as `select_cards`, `play_hand`, and `discard` only when the current screen makes them available ([state builder](https://raw.githubusercontent.com/Gdnaiteab/Balatro-MCP/main/mods/BalatroMCP/balatro_mcp/state.lua)).

Its action payload has `action`, optional `card_indices`, `index`, `area`, `mode`, `seed`, `stake`, and `blind` ([client source](https://raw.githubusercontent.com/Gdnaiteab/Balatro-MCP/main/mcp_server/src/balatro_mcp/client.py)). Card indices are **1-based**, unlike BalatroBot ([README action examples](https://raw.githubusercontent.com/Gdnaiteab/Balatro-MCP/main/README.md)). The mod manifest is version `0.1.0` and the repository has no GitHub release: **maintenance/versioned compatibility is UNKNOWN**. It should not be mixed with BalatroBot in one game installation or abstracted behind the milestone-1 runtime.

## Integration decisions and verification gates

- **Chosen game adapter:** BalatroBot v1.5.2 JSON-RPC, with runtime OpenRPC discovery and a captured real `gamestate` fixture before coding state reduction.
- **Chosen decision adapter:** TypeSafe `POST /v1/systemone`, one closed `choice` question, pinned `jev-1.13.0`, with API key supplied only through environment configuration.
- **No free-form execution:** only program-owned `GameAction` IDs are included in Choice criteria; response, state, index range, count, game state, and remaining-discard checks are all deterministic.
- **Out of scope:** all shop/joker/pack/consumable operations and every MCP runtime dependency.
- **Not yet verified:** successful mod loading, live HTTP connectivity, live state schema, successful play/discard execution, TypeSafe authentication, and end-to-end first-blind completion. These must be reported as not tested until actually run.
