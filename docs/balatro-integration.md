# Balatro integration

## Supported path

This PoC attaches to an already-running Steam copy of Balatro. At `MENU`, it starts the configured run through BalatroBot; if a run is already in progress, it takes over from the current supported game state. It does not install Balatro, launch Steam, use screen capture, or emulate mouse or keyboard input.

BalatroBot v1.5.2 is the selected adapter. Its upstream installation guide lists Balatro `v1.0.1+`, Lovely `v0.8.0+`, and Steamodded `v1.0.0-beta-1221a+` as prerequisites. On Windows, Lovely's `version.dll` is placed beside `Balatro.exe`; Steamodded and BalatroBot are installed beneath `%AppData%\Balatro\Mods`.

Sources: [BalatroBot installation](https://coder.github.io/balatrobot/latest/installation/), [Lovely](https://github.com/ethangreen-dev/lovely-injector), and [Steamodded](https://github.com/Steamodded/smods).

## Runtime gate

Before using this application, start `balatrobot serve`, launch Balatro through Steam, enter a fresh Small Blind, and verify `health` plus `rpc.discover` against `http://127.0.0.1:12346`. Save the returned OpenRPC document and a real `gamestate` response before claiming integration test coverage.

## Local Boss-constraint extension

This workspace extends the installed local BalatroBot mod at `%AppData%\Balatro\Mods\balatrobot\src\lua\utils\gamestate.lua` and its `openrpc.json`. Each blind now exposes `key` and `constraints`; Boss constraints cover the base-game Boss Blind set. Restart `balatrobot serve` after this change, then confirm a `gamestate` response includes `result.blinds.boss.constraints`. A BalatroBot upgrade may overwrite these local changes, so reapply or port the extension when upgrading.

`UNKNOWN`: the locally installed Steam build and mod versions have not been tested together in this workspace.
