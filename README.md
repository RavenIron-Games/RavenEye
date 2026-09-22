# RavenEye

A Valheim mod by [Raven Iron](https://github.com/RavenIron). Download on
[Hexium](https://valheim.hexium.gg/mods/RavenIronStudios/RavenEye).

**The vanilla map, for admins only, on a world that has none — with every player on it.**

For server owners. On a world running the `nomap` modifier, the people listed in the
server's `adminlist.txt` get the ordinary minimap and large map back, exactly as the game
draws them, and on it the ordinary player pins — name and icon — for every player online,
whether or not those players ever chose to share their position.

It does nothing for anyone else. Nothing new is drawn. Remove it and the world is exactly
what it was.

**Early release.** Seen working on a dedicated no-map server: the map appearing for an admin,
another player's pin moving, the revoke when an admin is removed from the list, the client
toggle. Not yet watched through: a dead player's marker holding for its three minutes, and a
listen host (not a dedicated server) as the admin. If either misbehaves for you, `raveneye
status` and the server log are the report to send.

---

## What an admin sees

The map you already know. Your own exploration (the game records it even on a no-map
world, so you see everywhere you have walked), your pins, the large map on `M`, the minimap
in the corner — and a player pin for every other player online, moving as they move, on the
same two-second cadence the game uses for shared positions. Names show on the large map when
you zoom in, as they do for shared positions in vanilla.

A player who has died stays on the map for three minutes at the place they fell, with
"(dead)" after their name — because on a world with no map, "help me find where I died" is
the request an admin hears most.

## Who is an admin

Whoever the **server** says. RavenEye reads the server's own `adminlist.txt` with the same
check the game uses before it honours a remote console command, a kick or a ban. There is
nothing to configure and no second list to keep in step. Add an ID and they have the map
within a few seconds; remove it and the map is gone within a few seconds.

A client cannot ask for the map, cannot claim to be an admin, and never sends the server
anything. The server sends the roster to each admin over that admin's own connection —
the one channel no other client can address — so no other player can intercept or forge it.

## Installing

**Server:** required. Nothing happens without it.

**Admins' clients:** required, to receive the roster and show the map. On a Gale-managed
client the plugin folder is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, not the Steam
folder.

**Everyone else:** nothing needed. If your modpack installs RavenEye on every client anyway,
it is inert for anyone not on the admin list — no map, no pins, no change of any kind.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
Built against Valheim 1.0.12. On a game at 1.0 or newer you need 0.2.0 or later; 0.1.0 loses its `raveneye` console command there.

## Playing honestly

An admin who wants to play their own no-map world the hard way sets `ShowMap = false` in
the client config, or types `raveneye map off` in the console. The server keeps granting; the
client declines. `raveneye map on` takes it back.

## The character's own `nomap`

Vanilla's `nomap` console command does two things in one keystroke: it toggles a per-
CHARACTER preference (keyed by the character's name, so it follows that character to every
world and every server), and, when typed by a host, it sets the world's no-map key. The
usual way to make a no-map world on a listen host is exactly that command — which means the
host's own character is opted out, and no grant will show them a map until it is cleared.
RavenEye respects that preference: it lifts the *world's* rule, never the character's.
`raveneye map on` clears it for you, and `raveneye status` says so in words when it is the
reason.

## On a world that has a map

By default RavenEye does nothing there: players' share-position choice stands. Set
`RevealWhenMapEnabled = true` on the server to also show admins the players who turned
sharing off.

## Seeing the terrain

RavenEye does not reveal the map itself — an admin's map starts with whatever that character
has explored, and player pins are drawn over the fog. To see the whole world, use the game's
own console: `devcommands`, then `exploremap`. It is one line, it already requires admin, and
the game has no way to un-explore, so it is a choice we would rather you made deliberately
than have made for you.

## When the map does not appear

Type `raveneye status`. It says why, in words:

- **"no roster has arrived since this join"** — this client is not on the server's
  `adminlist.txt`, or the server is not running RavenEye.
- **"the server revoked it Ns ago"** — you were removed from the list, or the server's
  feature was switched off.
- **"stale — last roster Ns ago, past the grace"** — the server stopped sending or stalled.
- **"Client.ShowMap is off"** — you switched it off. `raveneye map on`.
- **"THIS CHARACTER'S OWN MAP IS OFF"** — this character typed `nomap`. `raveneye map on`.
- **"server RavenEye v…"** and a rejection naming a format version — the server and this
  client run different RavenEye versions. Update the older side.

## Configuration

`BepInEx/config/com.raveniron.raveneye.cfg`. Every value says which side reads it.

| Section | Key | Default | |
|---|---|---|---|
| Server | `Enabled` | true | the whole feature, on this world |
| Server | `IntervalSeconds` | 2 | how often the roster goes out (0.5–5) |
| Server | `GraceSeconds` | 60 | how long an admin keeps the map if the server goes *silent*; a demotion does not wait for this |
| Server | `DeathGraceSeconds` | 180 | how long a dead player's last position stays on the map; 0 disables |
| Server | `RevealWhenMapEnabled` | false | on a world with a map, also show players who hide their position |
| Server | `VerboseLogging` | false | log every send (grants and revocations log regardless) |
| Client | `ShowMap` | true | use the map when the server grants it |

## Console

`raveneye status` — role, grant, and in words why the map is or is not showing.
`raveneye roster` — every player the roster carries (admins and hosts).
`raveneye map on|off` — use, or refuse, the granted map on this client; `on` also clears the
character's own `nomap` opt-out.

## Two things it cannot promise

**The map unlock is on the honour system.** The game keeps its no-map rule in a single flag
that any client mod can flip in one line, with or without RavenEye. What RavenEye actually
protects is the *roster*: the positions of players who are not sharing them, which only the
server can supply, and which it supplies to admins alone.

**A pin is where the server last saw a player.** That position is reported by the player's
own game, roughly every two seconds. It is "where is everyone", not evidence for a
moderation decision.

## What it does not do

No UI of its own, no overlay, no list, no arrows. It never touches the environment,
materials or textures, adds no prefabs, and writes nothing into the world or into any
character's save. The roster carries a name, a character id, a position and a dead flag —
and nothing else. It is not a cheat for players: a client with RavenEye and no admin entry
receives nothing.

## Compatibility

RavenEye patches two things, both as postfixes that yield to any other mod: the game's own
map-permission decision (`Game.UpdateNoMap`) and the list of players to pin
(`ZNet.GetOtherPublicPlayers`). It does not touch `EnvMan`, map rendering or world
generation, so season and weather mods (Seasonality, Seasons) are unaffected. A mod that
unlocks the map for everyone makes RavenEye's unlock redundant; the pins still work.

One vanilla behaviour changes for a granted admin exactly as it would on a map world:
reading a Vegvisir pins the location and opens the map, instead of turning your head toward
it.

Cairn and RavenEye are complementary: Cairn is for players finding their way on a world with
no map; RavenEye is for the person on duty.

RavenEye shares a name's worth of family resemblance with The Raven's Call and nothing else:
no code, no data, no connection between them. One shows admins where people are right now;
the other keeps a server's chronicle.

---

## Support Raven Iron

Every Raven Iron mod is free, and stays free — all of it, always. Nothing is held
back for patrons, and nothing ever will be.

If you'd like to help cover server hosting and test hardware:

- **Website** — <https://ravenirongames.com>
- **Patreon** — <https://www.patreon.com/cw/RavenIronGames>
- **Discord** — <https://discord.gg/AGKDEurAVa> — a channel per mod, and where the
  testing happens
