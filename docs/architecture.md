# Architecture

```text
BalatroBot GameState -> BalatroStateReducer -> BlindDecisionState
                                        -> CandidateActionGenerator
                                        -> Ollama JSON decision
                                        -> ActionValidator -> BalatroBot RPC action
```

`JevBalatroBot.Core` owns game concepts, allowed action shapes, validation, decision records, and the game/decision interfaces. It has no HTTP, console, Ollama, BalatroBot, Steamodded, or Steam dependency.

`JevBalatroBot.Balatro` owns JSON-RPC and state reduction. `JevBalatroBot.Jev` currently owns the Ollama HTTP adapter. `JevBalatroBot.Agent` owns candidate-space reduction, strategy scoring, fallback selection, pre-dispatch revalidation, and JSONL logging. The console project composes these pieces.

Candidate generation enumerates legal play and discard selections internally, then retains at most 32 candidates of each action type by round-robin coverage of poker-hand categories, discard sizes, and target-card combinations. It does not rank or eliminate a legal candidate by a program-owned build strategy. Before a hand action, Jev selects an available poker hand as the current build, sees the regenerated candidate list, commits to a tactical intent, target hand, and expected number of hands to clear, then chooses the legal action. The runner injects these fields plus a bounded same-run memory into state, validates, and executes a valid Jev action without replacement. `DecisionAuditor` records plan/action, target-hand, and risky-play divergences but never changes the selected action. For target-card consumables and pack cards it generates valid target combinations. Only a missing or invalid Jev response invokes the deterministic fallback: leave a shop, skip a pack, or play the highest base-score hand. Jev cannot create a command, endpoint, or card list.

Each `PlayHand` criterion also carries a vanilla base-score preview, the post-play blind gap, remaining hands, and build alignment. It deliberately excludes Joker and card-effect triggers; it is decision context, never a program-owned ranking or action override. Discard and consumable criteria disclose when they have no immediate score effect.

`DecisionLogWriter` creates `runs/<run-id>/` below the directory of the configured log path. Each run has its own full decision JSONL, human-readable event log, and final summary JSON; no gameplay history is appended to a shared run file.

The local BalatroBot Lua integration turns base-game Boss effects into structured state before candidate generation. The .NET runner does not inspect Boss names or localized effect text: it generically applies hard `PlayHand` constraints such as a minimum card count, one hand type per round, and one use per hand type per round. This is a game-rule legality filter, not a strategy ranking or action override.

Fallback is lexicographically the first currently legal hand candidate, leaves the shop on a failed shop decision, and skips an opened pack on a failed pack decision. It is recorded as `DecisionSource.Fallback`. A decision that becomes stale before dispatch is aborted and never executed.

The run loop takes over from any supported state. From `MENU`, it starts the configured deck/stake; from `BLIND_SELECT`, it selects the available blind; from `ROUND_EVAL`, it cashes out; from `SHOP`, Jev chooses shop actions (up to 10 before the runner leaves); and from `SMODS_BOOSTER_OPENED`, Jev chooses a pack card or skips it. It ends only at `GAME_OVER`. Blind skipping and card rearrangement are intentionally outside this scope.
