# Strike Range online (P6)

Up to 8 pilots on the same range, competing for the same targets, over the existing race network (Netcode for
GameObjects named messages, Unity Transport). Two ways in:

- **Internet:** the existing Unity sessions with Relay (*Online > Host a race / Quick race / code*), track *Strike Range*.
- **LAN / direct address (new):** *Online > Host a LAN race* on one PC; the others type its address into *Host address*
  and press *Join LAN*. No Unity account, no sign-in, no internet. Port 7777 (UDP); `address:port` for another port.

The host picks the range in the lobby's track list; difficulty and bots come from the host's own *Strike Range* venue
page (Settings are saved there).

## How it works

| Topic | What happens |
|---|---|
| Topology | Host-client. The host's PC is the referee; everyone (the host too) talks to it through messages |
| Quads | Each PC flies its own quad locally and sends 20 snapshots a second (as in online races); the others are interpolated |
| Targets | The host sends the round setup (seed, difficulty, bots) before the load. Every PC builds the same `RangeDirector` and runs it on the shared clock (server time − GO). Positions never travel |
| Hits | The pilot's PC claims a hit (target slot, the target's life number, time, position, speed). The host's `StrikeReferee` checks the slot, the life, that the target is still up, the speed (≥ 8 m/s), the time (within the round, at most 1 s old, at most 0.25 s ahead) and the position (within 7 m of the target at that time). **The first valid claim to arrive wins.** The host confirms to everyone; each PC disables the target at the confirmed time, so respawns match everywhere |
| Turned-down claims | Only the claimer is told ("Too late: already hit" / "Not counted"). The launch counts as a crash |
| Scores | Kept by the host only (`StrikeReferee.Scores`); every change is sent to everyone. All PCs show the same scoreboard |
| Bots | 0–3, run on the host and fly its range; their poses go out at 10 Hz, their hits and scores like a pilot's |
| Leaving | A pilot who leaves drops out of the standings and the round goes on. If the host leaves, everyone returns to the menu with a notice |
| After the round | Results on every PC; the host presses T (L-stick click) to take everyone back to the lobby. A rematch gets a new seed |

Code: `Scripts/Strike/StrikeNet.cs` (wire format, referee), `Scripts/Game/OnlineStrikeDriver.cs` (the online round),
`NetRace` (mode messages, direct sessions), `OnlineLobby` (LAN mode beside Unity sessions), `StrikeSession` (`IStrikeLink`).

## Tests

`StrikeNetTests` (EditMode, no network):

- every message round-trips; event messages are 12–25 bytes;
- the referee: a valid claim scores and disables the target; first valid claim wins; slow, far, high, old, future,
  stale (after a respawn) and unknown claims are turned down;
- latency 30 / 150 / 350 / 800 ms × clock error −0.2 / 0 / +0.2 s on a moving target: all count;
- **a simulated round:** host + 3 clients, each with its own director, frames at 90 / 72 / 60 / 30 fps, every message
  delayed 20–250 ms, pilots going for the same targets. At the end every PC has the same targets (life, state, place,
  kind, who hit it) and the host's scoreboard is everyone's. One run: 107 hits, 28 contested claims turned down.

**Play mode check (real transport, LAN host loopback in the editor):** hosted a LAN race, started the range: setup,
load, ready, GO and the round ran; host bots' hits were confirmed and scored through the messages; no errors.

## Network cost per pilot

Payload only (Netcode and transport headers come on top, roughly 10–20 bytes a packet):

| Stream | Rate | Bytes | Per second |
|---|---|---|---|
| Own snapshot up | 20 Hz | 58 | 1.2 KB/s up |
| Others' snapshots down | 20 Hz × (pilots − 1) | 66 | 9.2 KB/s down with 8 pilots |
| Strike events (claims, confirms, scores) | a few per minute | 12–25 | < 0.1 KB/s |
| Host bots' poses | 10 Hz | 1 + 30 per bot | 0.9 KB/s with 3 bots |

The host uploads every pilot's stream to every other pilot: with 8 pilots about 74 KB/s (0.6 Mbit/s). Measured in the
loopback round with 2 bots: 0.61 KB/s of strike traffic.

## LAN test (two PCs)

1. Both PCs on the same network; allow *Ushqyr* through Windows Firewall when asked (private networks).
2. PC A: *Online > Host a LAN race*. The lobby shows its address(es), e.g. `192.168.1.20`.
3. PC B: *Online*, type `192.168.1.20` into *Host address*, *Join LAN*. Both lobbies list two pilots.
4. PC A: track *Strike Range*, *START RACE*. Both load the range; the pad lights count down together.
5. Check: the same targets in the same places on both screens; a hit on one PC disables the target on the other; both
   scoreboards agree; with bots on (PC A's *Strike Range* page), PC B sees them fly.
6. Close the game on PC B mid-round: PC A carries on, B leaves the standings. Restart B, rejoin; then close A: B returns
   to the menu with "The race was closed."

One PC, two copies (smoke test): `Ushqyr.exe -lan-host strike-steppe -online-autostart 2 -online-quit`
and `Ushqyr.exe -lan-join 127.0.0.1 -online-quit`; both logs print `[Online] strike ...` lines and
the same `targets gen-sum` at the end.

## Not done yet

- The internet (Relay) path for the Strike Range is the existing session code and has not been run with the range.
- Bots only fill in when the host sets them; they don't yet top up empty slots automatically.
- No online leaderboard for the range (local boards only).
