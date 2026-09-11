# Changelog

## 0.2.0

Valheim 1.0. **If you updated the game, 0.1.0 was broken and this is the fix.**

- **Rebuilt for Valheim 1.0.12.** Nothing about the map, the roster, the admin gate or the death
  grace changed. The game did: 1.0.7 added a parameter to `Terminal.ConsoleCommand`'s constructor,
  and .NET binds a call like that by its exact signature at runtime, so the `raveneye` console
  registration in the 0.1.0 binary threw `MissingMethodException` on a 1.0.x game. Losing that
  command matters more here than in most mods — it is the instrument that tells an admin *why*
  the map is or is not showing, and "the map did not appear" has at least eight causes with one
  symptom.
- **Source link fixed.** The repository moved to the `RavenIron-Games` organisation, so the
  "Source" link on this page pointed at a URL that no longer resolves.
- **Why 0.1.0 looked healthy.** It compiled clean against 1.0.7 the day the update landed. A clean
  build proves the source matches today's game — it says nothing about a DLL built weeks earlier.
  The break was found by reading the shipped binary's own reference table and resolving each entry
  against the live game assemblies.


## 0.1.0

First release.

**The vanilla map, for admins only, on a world that has none — with every player on it.**

- **Admins get the ordinary map back** on a `nomap` world: minimap, large map, their own
  exploration and pins, drawn by the game's own code. Nothing new on screen.
- **Every online player is pinned**, name and icon, whether or not they share their position
  — the same vanilla player pin, on the same two-second cadence vanilla uses.
- **A dead player stays on the map** at the place they fell, marked "(dead)", for three
  minutes (`DeathGraceSeconds`) — the answer to "where did I die?" on a world with no map.
- **The server decides who is an admin**, from its own `adminlist.txt`, with the same check
  the game applies to remote commands, kicks and bans. Edits take effect within seconds:
  a demotion is sent explicitly and relocks within one interval.
- **Non-admins see nothing.** A client with the mod and no admin entry receives no data and
  the map stays locked. Only a name, a character id, a position and a dead flag are ever sent.
- **Forge-proof by construction.** The roster travels on each admin's own server connection,
  not the shared routed channel other clients can address.
- **Silence is only a backstop.** If the server crashes or stalls, the map survives
  `GraceSeconds` (60) and then lapses — generous on purpose, so an autosave hitch never slams
  the map shut under an admin's cursor.
- **Playing honestly is a toggle**: `ShowMap = false` or `raveneye map off` declines the
  grant on your client. The game's own per-character `nomap` opt-out is respected, and
  `raveneye map on` clears it.
- **Does nothing on a world that has a map** unless `RevealWhenMapEnabled` is set, in which
  case admins also see players who hide their position.
- **Terrain is not revealed.** The game's `devcommands` + `exploremap` remain the deliberate
  way to do that; the game cannot un-explore.

Console: `raveneye status | roster | map on|off`. `status` states the grant's reason in
words — not on the adminlist, server without the mod, revoked, stale, toggle off, the
character's own opt-out, or a version mismatch naming the side to update — because from the
seat they all look the same.

Built against Valheim 0.221.12. 143 off-game tests, each load-bearing one proven to fail
without its fix. Designed under a three-critic adversarial review; `docs/DESIGN.md` records
what it changed.

**Early release.** Verified live on a dedicated no-map server on 2026-09-06: the map appearing
for an admin, another player's pin moving, the explicit revoke on removal from the admin list,
and the client toggle. Not yet watched through: a dead player's marker holding for its three
minutes, and a listen host as the admin.
