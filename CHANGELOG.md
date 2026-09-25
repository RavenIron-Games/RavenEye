# Changelog

## 0.2.2 — 2026-09-25

Updated due to 1.0.16 Patch.

## 0.2.1

Two fixes and a README correction, plus the housekeeping listed under "Also in this release".
Who gets the map, the roster, the admin gate and the death grace work as before. The 0.2.1 DLL
was built from the commit tagged `v0.2.1`; the GitHub release (<https://github.com/RavenIron-Games/RavenEye/releases/tag/v0.2.1>) names that commit and gives the DLL's md5.

- **No no-map achievement progress while RavenEye grants you the map.** The game decides that
  you are exploring without a map from the world's no-map setting alone, and RavenEye leaves
  that setting in place. So an admin, a host or a solo player sailing to the world's edges with
  RavenEye's map up was counted toward the game's no-map exploration achievement. RavenEye now
  tells the game that trip does not count while RavenEye grants you the map, even if your
  character's own `nomap` has it hidden. With RavenEye's map switched off (`ShowMap = false`
  or `raveneye map off`), the game counts as it always did.
- **One broken patch no longer takes the whole mod down.** If a future Valheim update renames
  or removes one of the game methods RavenEye hooks, only that hook stops working and the rest
  carries on. The BepInEx log names the part that could not load. Before, the whole mod stopped
  at start-up. This does not cover every change an update can make: if one renames or removes
  a whole game class that the rest of RavenEye also uses, a game method RavenEye calls rather
  than hooks, or a game field it reads or writes, more than that one part can stop working.
  (Corrected in 0.2.2.)
- **README correction: a host or solo player gets the map on their own world.** The README
  said RavenEye does nothing for a player who is not on an admin list. That is only true for a
  player who joins someone else's server. Whoever runs the world is its admin, so a player
  hosting from their own game, or alone on a single-player no-map world, gets the map with
  RavenEye installed. To keep a solo no-map run map-less, set `ShowMap = false`, type
  `raveneye map off`, or leave RavenEye out of that profile.

Also in this release:

- **The DLL no longer carries the build machine's folder path.** Every earlier build embedded
  the absolute path of its debug-symbols file, a path that included the build machine's user
  name. The build now maps its source folders to a neutral placeholder, so the shipped DLL names
  no local path.
- **A "Support Raven Iron" section in the README**, with the Raven Iron website, Patreon and
  Discord links. Every Raven Iron mod is free and stays free; nothing is held back for patrons.
- **The store page's link now goes to the Raven Iron website**; under 0.2.0 it was the Source
  link to the GitHub repository, which stays at <https://github.com/RavenIron-Games/RavenEye>.
- **Built against Valheim 1.0.15.**

**Tested in game on 2026-09-24**, on the same code as this release (commit `e189400`; only
documents and the store page's website link changed after it) — DLL md5
`485fe614040d9953e2ae3e1de4950a5f`, 43,008 bytes — on a Valheim 1.0.15 dedicated server
(crossplay) with one client, and on a local no-map world played as its host. Boot on both sides
showed `patches=4`, nothing FAILED. On the local no-map world, played as host, the map was
granted to the host at the start; while it was granted, a trip to the world's edge counted
`ExploreWest` and never `ExploreWestNoMap` (the game's own stat lines): the achievement guard
held. With `raveneye map off` the same trip counted `ExploreWestNoMap` as it always did, and
`raveneye map on` granted the map again. Joining the dedicated server's own world, which has a
map, correctly granted nothing and left the map as the game set it. Not tried in game: **a dead
player's pin holding for its full three minutes**; **a second player's pin on a listen host's
map** (this run saw the host granted as authority, not a second player joining that host); the
achievement guard seen as an admin *client* on a dedicated no-map server rather than as the host
(the server used for this run has a map); the guard with the character's own `nomap` hiding the
map (there the credit is withheld either way, because the grant check does not read that
setting); a host who made the world no-map by typing `nomap` (their own character's map is then
off until `raveneye map on`; this run's world was not made that way); the platform achievement
itself (the guard withholds the four `Explore…NoMap` stats; that these are what the game's
no-map achievement counts is read from their names, not from the game's achievement data); and a
forced patch-install failure, which cannot be forced without changing the code (7 off-game tests
cover the rule; `patches=4` with nothing FAILED is the regression check in game). Every
in-game reading in this paragraph comes from the BepInEx and game log lines; only the
`raveneye status` replies, which do not reach the client log, were read on screen. Off-game: 154 checks, 0 failed.


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
