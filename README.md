# Balatro Agent PoC

An experimental .NET 8 console application that lets either a local Ollama model or TypeSafe Jev choose from program-generated, revalidated Balatro actions while the application automatically progresses a full run.

## Run

1. Install and start the upstream BalatroBot stack; see [Balatro integration](docs/balatro-integration.md).
2. From this directory, run `dotnet run --project src/JevBalatroBot`.

When BalatroBot is at `MENU`, the application starts a run using the configured `BLUE` deck and `WHITE` stake. If the game is already in progress, it takes over at the current game state. It automatically selects blinds and cashes out. The configured decision provider chooses legal plays/discards, shop purchases/sales/rerolls, consumable uses, and booster-pack selections. The program limits each shop to 10 decisions, then leaves automatically. Invalid model answers fall back to leaving a shop or skipping a pack. The console prints every decision and all automatic transitions.

The selected provider is the run's strategist. During hand selection, every decision has three sequential model requests: it first chooses one concrete build from the poker hands currently available in the game state; it then commits to a tactical intent (`score_now`, `draw_for_build`, or `setup`), a target hand, and an estimated number of hands needed to clear the blind; and it finally chooses one legal operation. Ollama uses JSON Schema output and private model reasoning; Jev uses its closed choice API. The program supplies a diverse, capped set of legal candidates, validates the chosen action immediately before execution, and performs game-state transitions. It never replaces a valid model choice. Only an unavailable or invalid model response uses a minimal fallback: leave the shop, skip a pack, or play the highest deterministic base-score hand.

For hand decisions, each candidate includes a vanilla base-score preview, the resulting blind gap, remaining hands, and whether its detected hand matches the model build intent. The preview intentionally excludes Joker and card-effect triggers, which remain the model's strategic judgment. Discard and consumable candidates state that they do not score immediately.

The local BalatroBot integration exports the active Blind's canonical key plus structured `constraints`. It contains every base-game Boss Blind effect, including penalties and card debuffs for the model to evaluate. The candidate generator applies only universally hard play restrictions from those constraints (minimum cards, one hand type per round, and one use per hand type per round); it does not parse effect text or special-case Boss names. The console and JSONL decision log show every constraint.

Configuration defaults are in `src/JevBalatroBot/appsettings.json`. The default provider is Ollama at `http://127.0.0.1:11434/api/chat` using `qwen3:14b` with a 120-second timeout. Switch providers in `appsettings.local.json` or with `DECISION_PROVIDER`:

```json
{ "Decision": { "Provider": "Ollama" } }
```

```json
{ "Decision": { "Provider": "Jev" }, "Jev": { "ApiKey": "your-key" } }
```

Jev uses `https://api.typesafe.ai/v1/systemone`, model `jev-1.13.0`, and `JEV_API_KEY` can override the local key. Other environment overrides include `BALATROBOT_ENDPOINT`, `OLLAMA_ENDPOINT`, `OLLAMA_MODEL`, `JEV_ENDPOINT`, `JEV_MODEL`, `JEV_LOG_PATH`, `BALATRO_DECK`, `BALATRO_STAKE`, and `BALATRO_SEED`. `JEV_LOG_PATH` is retained as a legacy log-root variable; with its default value, each run is written to `logs/runs/<run-id>/` containing `decisions.jsonl` (full state, model responses, plan fields, selected card IDs, and audit findings), `run.log` (readable event stream), and `summary.json` (final outcome and metrics). The next model decision receives only the last 12 decisions of this same run as `JevRunMemory`; this memory resets at every new run.

## Fixed-seed evaluation

Keep BalatroBot running, then run exactly ten seed strings that you have verified Balatro accepts:

```powershell
.\scripts\Invoke-SeedEvaluation.ps1 -Seeds 'seed-1','seed-2','seed-3','seed-4','seed-5','seed-6','seed-7','seed-8','seed-9','seed-10'
```

The script returns Balatro to the main menu before every seed, runs them sequentially, keeps each run's log in a separate seed directory, and writes one `evaluation-summary.json`. It deliberately does not invent seed values: replace the placeholders with actual accepted Balatro seeds. Re-run the same ten seeds after a change and compare the two summaries.

## Verification

```text
dotnet restore JevBalatroBot.slnx
dotnet build JevBalatroBot.slnx --no-restore
dotnet test JevBalatroBot.slnx --no-build
```

The automated tests use the checked-in BalatroBot-shaped fixture plus a mocked Ollama API. A local Ollama JSON-schema request is also verified during setup. Steam/BalatroBot end-to-end gameplay verification is not included.
# balatro-autopilot
