# BalatroBot protocol

BalatroBot v1.5.2 exposes JSON-RPC 2.0 over HTTP at `http://127.0.0.1:12346`. Requests use `Content-Type: application/json` and responses contain either `result` or `error`.

```json
{ "jsonrpc": "2.0", "method": "gamestate", "id": 1 }
```

The full-run mode uses:

| Method | Parameters | Required state |
| --- | --- | --- |
| `gamestate` | none | any |
| `play` | `{ "cards": [0, 2] }` | `SELECTING_HAND` |
| `discard` | `{ "cards": [0, 2] }` | `SELECTING_HAND` |
| `use` | `{ "consumable": 0, "cards": [0, 2] }` | `SELECTING_HAND` or `SHOP` |
| `buy` | `{ "card": 0 }`, `{ "voucher": 0 }`, or `{ "pack": 0 }` | `SHOP` |
| `sell` | `{ "joker": 0 }` or `{ "consumable": 0 }` | `SELECTING_HAND` or `SHOP` |
| `reroll` | none | `SHOP` |
| `pack` | `{ "card": 0, "targets": [0] }` or `{ "skip": true }` | `SMODS_BOOSTER_OPENED` |
| `start` | `{ "deck": "BLUE", "stake": "WHITE" }` | `MENU` |
| `select` | none | `BLIND_SELECT` |
| `cash_out` | none | `ROUND_EVAL` |
| `next_round` | none | `SHOP` |

Indices are zero-based. `play` permits one to five cards. The PoC maps `ante_num`, `money`, `round.{hands_left,discards_left,reroll_cost,chips}`, active `blinds.*`, `hand.cards`, `jokers`, `consumables`, `shop`, `vouchers`, `packs`, and `pack` to the transport-independent `BlindDecisionState`.

There is no upstream `legal_actions` endpoint. Candidate generation and validation therefore use the just-read hand's indices and stable BalatroBot card IDs. The runner reads state again immediately before dispatch and rejects stale candidates.

Sources: [API reference](https://coder.github.io/balatrobot/latest/api/), [upstream OpenRPC source](https://raw.githubusercontent.com/coder/balatrobot/main/src/lua/utils/openrpc.json).
