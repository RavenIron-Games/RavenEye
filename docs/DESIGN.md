# RavenEye — design document

*The vanilla map, for admins only, on a world that has none — with every player on it.*

Written 2026-09-06 alongside 0.1.0. This records why the mod is shaped the way it is, what
the engine actually does (read from decompiled bodies, Valheim 0.221.12), what a
three-critic adversarial review changed before a line shipped, and what it rejected.

---

## 1. The request

> an admin only map just like the vanilla that shows player positions for no map worlds

Interpretation: on a world with the `nomap` modifier, players listed in the server's
`adminlist.txt` get the ordinary vanilla minimap and large map back, and on it every online
player as a vanilla player pin, regardless of the players' share-position setting.
Non-admins see no change. Nothing is drawn that vanilla does not already draw for a
map-enabled player.

Ragnarok's Wrath's locked decisions forbid player-facing UI there; Cairn's forbid maps. So:
its own mod, built on Cairn's project template and the studio house style.

## 2. Engine facts (decompiled, bodies read)

| # | Fact | Where |
|---|---|---|
| F1 | `Game.m_noMap` (public static) is written in exactly one place (`Game.UpdateNoMap`) and read in exactly one place (`Minimap.SetMapMode`, which coerces any requested mode to None). Verified assembly-wide by the critique over a 602-type decompile. | Game.cs, Minimap.cs |
| F2 | `Game.UpdateNoMap()` = `ZoneSystem.GetGlobalKey(NoMap)` OR `PlatformPrefs.GetFloat("mapenabled_"+name,1)==0`, then `Minimap.instance.SetMapMode(...)`. Callers: first spawn (behind one-shot `m_firstSpawn`) and `UpdateWorldRates` (global-key sync). | Game.cs |
| F3 | `Minimap.Update` runs `UpdateExplore` before any mode logic; when mode is None it calls `SetMapMode(Small)` every frame. Exploration is recorded on no-map worlds. | Minimap.cs |
| F4 | `Minimap.UpdatePlayerPins` → `ZNet.GetOtherPublicPlayers(list)` every frame; rebuilds pins on COUNT change; matches by INDEX; smooths at 200 m/s only when the name matches. | Minimap.cs |
| F5 | `ZNet.GetOtherPublicPlayers` appends `m_players` entries with `m_publicPosition` and a non-None, non-local character id. `ZNet.PlayerInfo` is a public struct with public fields. | ZNet.cs |
| F6 | Clients send `m_referencePosition` to the server every 2 s regardless of the share toggle (`SendServerSyncPlayerData` → `peer.m_refPos`, `peer.m_publicRefPos`). Clients also push every changed owned ZDO to the server (`ZDOMan.CreateSyncList`, client branch uses `m_clientChangeQueue`, not the server's ref position). | ZNet.cs, ZDOMan.cs |
| F7 | Vanilla `SendPlayerList` writes a position only when `m_publicPosition`; `RPC_PlayerList` replaces `m_players` wholesale every 2 s. | ZNet.cs |
| F8 | `ZNet.IsAdmin(string hostName)` is PUBLIC and its body is `ListContainsId(m_adminList, hostName)` — the check vanilla applies to remote commands, kicks and bans; accepts prefixed and bare ids. `PlayerIsAdmin(PlatformUserID)` checks one form only. `m_adminList` exists for dedicated and listen hosts. `SyncedList` re-reads on mtime change at most every 10 s, and its `Load` clears-then-reads, swallowing exceptions. | ZNet.cs, SyncedList.cs |
| F9 | `ZRoutedRpc.RPC_RoutedRPC`: the server forwards to the packet's `m_targetPeerID` with the packet's `m_senderPeerID`; target 0 broadcasts. `ZRpc.Register` removes-then-adds; `ZRpc.HandlePackage` ignores unknown methods. `ZNet.GetServerRPC()`, `GetServerPeer()`, `GetPeers()`, `LocalPlayerCharacterID`, `GetReferencePosition()`, `GetAdminList()` are public. | ZRoutedRpc.cs, ZRpc.cs, ZNet.cs |
| F10 | `ZPackage` primitives: int/bool/float via BinaryWriter; string via BinaryWriter (7-bit length, UTF-8); ZDOID as long UserID then uint ID; Vector3 as three floats. `Write(ZPackage)` copies `GetArray()`, so one packet can be invoked to many peers. | ZPackage.cs |
| F11 | Death: `Player` calls `Game.RequestRespawn(10f, afterDeath: true)`; `_RequestRespawn` sets the character id to None and destroys the player; vanilla's pins drop the player for that window. | Player.cs, Game.cs |
| F12 | Two nomap-adjacent gates keyed on `Minimap.m_mode == None`: chat ping distance (Chat.cs) and `Game.RPC_DiscoverLocationResponse` (Vegvisir turns the head instead of pinning). | Chat.cs, Game.cs |
| F13 | `ZDOMan.instance`, `ZDOMan.GetZDO(ZDOID)`, `ZDO.GetPosition()`, `PlatformPrefs.GetFloat/SetFloat`, `Game.instance.GetPlayerProfile().GetName()`, `ZoneSystem.GetGlobalKey(GlobalKeys)`, `Minimap.instance/m_mode/SetMapMode`, `Player.m_localPlayer/GetPlayerName`, `Terminal.ConsoleCommand`, `ZNetPeer.*` are all public in the real assembly. Private, and therefore avoided: `ZNet.m_peers/m_adminList/m_players/m_referencePosition/m_characterID/m_routedRpc/ListContainsId`, `ZRoutedRpc.m_id/GetServerPeerID`, `Minimap.m_playerPins`, `ZRpc.m_functions`. | real/*.cs |

## 3. Architecture

One role-aware DLL. The roles are told apart at runtime.

**Server (dedicated or listen host), every `IntervalSeconds`:**
1. Gates: `Enabled` and (`NoMap` key set OR `RevealWhenMapEnabled`). Closed → build
   nothing, send one revoke to every peer currently granted, done.
2. Build the roster: every ready peer with a character id (name, id, position); the host's
   own character on a listen host; and, for `DeathGraceSeconds`, a snapshot of anyone whose
   id went None (taken from the last cadence that still had it — the live reference position
   is reassigned while dead and is never served).
3. For each ready peer: if `ZNet.IsAdmin(host)` → `peer.m_rpc.Invoke(roster)`; grant logged
   on first send. Else, if it was granted last time → one revoke packet, logged.
4. Position source: the character's ZDO position when the server holds the ZDO (it does, F6),
   else `peer.m_refPos`.

**Client (anything with a renderer):**
1. Register the receiver on `ZNet.GetServerRPC()` — the one socket a client has. Never on the
   server role.
2. Receipt: parse; a granting packet replaces the cache and stamps `LastReceipt`; a revoke
   clears the cache and resets `LastReceipt` to Never, stamping `LastRevoke`.
3. Grant = `ShowMap && renderer && (authority || (server peer exists && roster within grace))`.
   Authority = a server whose two gates are open.
4. Converge, twice a second: `desired = granted ? personalOptOut : (worldKey || personalOptOut)`;
   if `Game.m_noMap != desired`, write it and call `SetMapMode` (Small if unlocking from None,
   None if relocking). Direct write, not `Game.UpdateNoMap()`, which would recurse through our
   postfix and force `SetMapMode(Small)` on every correction.
5. Postfix on `Game.UpdateNoMap`: when granted, recompute without the world key. Corrects
   vanilla's own cold calls (first spawn, key sync) without waiting half a second.
6. Postfix on `ZNet.GetOtherPublicPlayers`: when granted, append roster entries not already
   present by character id and not the local character; dead entries get "(dead)" on the name.
   Vanilla draws them.

**Wire format (v1):** `int formatVersion, string serverVersion, bool granted, float graceSeconds,
int count, count × (string name, ZDOID id, Vector3 pos, bool dead)`. A revoke has zero
entries, and a revoke carrying entries is malformed. Non-finite floats are malformed. Count
outside 0..64 is malformed before any allocation. A malformed packet leaves the previous
roster intact.

## 4. Security model

- **Confidentiality of positions**: only the server builds the roster; it sends to admin
  peers only, over each peer's own socket. No client→server message exists. A non-admin
  client with the mod receives nothing.
- **Integrity**: the direct `ZRpc` channel has one possible origin per socket. Routed RPCs
  were rejected because any client can make the server relay a forged packet to any target,
  including all admins, with any sender id (F9).
- **Fail closed**: the admin check throwing means nobody is granted, logged once.
- **Two honest limits** (in the README): the map unlock is client-side and unenforceable —
  `Game.m_noMap` is a public static any plugin can flip; what is protected is the roster.
  And a pin is where the server last saw a player, self-reported by that player's client.

## 5. The critique, and what it changed

Three independent critics (network security; engine correctness and house style; product
and operations) reviewed the brief against the decompiled sources; a synthesizer verified
their disagreements and decided the open questions. Verdict: sound seam, eleven required
changes, one architectural.

**Changed:**
- **Transport** — from `ZRoutedRpc` to the peer's own `ZRpc`. Any client could otherwise
  forge a roster to any admin, or broadcast one, and the server itself would parse client
  bytes (routed target = own id). One change closed four defects.
- **Admin check** — from reflection into the private `ListContainsId` to the public
  `ZNet.IsAdmin(string)`. The first draft's grep of the public surface was `head`-truncated
  and missed the wrapper. Deleting the reflection deleted the mod's only rule-5 surface.
- **Unlock** — from edge-triggered (re-run `UpdateNoMap` on a grant transition, guarded on
  `Player.m_localPlayer != null`) to convergent. A revoke landing while the admin is dead
  would have been consumed and lost; the first-spawn recompute is a one-shot and would never
  have relocked them.
- **Revoke** — from silence-only (4×interval, ~8 s) to an explicit revoke packet plus a
  generous absolute grace (`GraceSeconds` 60, clamped 10–120). Eight seconds is inside a
  dedicated-server autosave stall; a false revoke slams a Large map shut under the cursor.
- **Gates** — moved into the builder, so a listen host reading the roster directly cannot
  bypass `Enabled` or `RevealWhenMapEnabled`; the host's grant uses the same two gates.
- **`RevealWhenMapEnabled`** — default false (was `RevealOnMapWorlds` true). Unanimous: a
  tool named for no-map worlds should do nothing on any other kind until told to.
- **Versioning** — format version inside the payload, RPC name stable. A name suffix no-ops
  silently, indistinguishable from "server has no mod"; the payload version names the side to
  update.
- **Death** — serve a last-known position, marked dead, for `DeathGraceSeconds`. "Where did I
  die?" is the commonest admin request on a no-map world and the brief dropped the player
  at exactly that moment.
- **`raveneye map on`** clears the character's own `mapenabled_` pref, because the standard
  way to make a no-map world on a listen host — typing `nomap` as host — opts the host's own
  character out in the same keystroke. The archetypal first user would otherwise be granted
  and stay dark.
- **Status** states the grant's reason in words; a boolean collapsed eight failures. Every
  value names its source, because a roster of zero entries is what an empty server looks like
  and what a never-received roster looks like.
- **Admin-list-empty instrument**: `SyncedList.Load` can strand the list empty after a
  failed read (file open in an editor) until the mtime changes, with no vanilla log line.
  The server warns once when the list reads empty while players are online; status prints
  the count.
- **Position source** — the character's ZDO when held, else the reference position.
- **Classification** — the `Game.UpdateNoMap` postfix is recorded as a narrow named exception
  to house rule 1, not as a decorator: it corrects a global static after a void method.
- **F12** corrected: two nomap-adjacent gates, not one.
- **Name** — Heimdall → Vantage → RavenEye. Heimdall collided with an existing Thunderstore
  mod and read as off-register for the studio; the panel picked Vantage; RavenIron then chose
  RavenEye, spelled as one word because that is what their logo says, after checks on Hexium
  (searches "raven" and "eye"), Thunderstore and Nexus found it free on 2026-09-06.

**Rejected, with reasons checked:**
- "Vanilla resets `m_noMap` a third time at Game.cs:1383" — that line is the publicizer's
  materialised static constructor; the real assembly has no such statement.
- "AND the client grant with `LocalPlayerIsAdminOrHost()`" — it falls through to
  `PlayerIsAdmin`, which checks one id form only and would reject a bare Steam64 admin.
- "The client-side grant being self-declarable is a blocker introduced by this design" —
  `Game.m_noMap` is a public static any plugin can flip; the hole predates the mod. The
  roster is what is protected, and the transport change protects it.
- "Register routed handlers from a `ZNet.Awake` postfix" — superseded by the transport change.
- "Guard uid == 0 / Everybody" — unreachable once the send is on `peer.m_rpc`.
- "Precompute the merge on receipt" — the merge depends on vanilla's per-frame list (dedupe
  against sharing players and self); it is O(n·m) over ≤ 10 entries with no allocation beyond
  list growth. Not worth restructuring.
- **`RevealTerrain` in any form** — `ExploreAll` writes into the character's per-world map
  data and the game cannot un-explore; it would survive demotion and uninstall on a world
  whose point is fog of war. Vanilla's `devcommands` + `exploremap` exist and require admin.

### Post-review fixes

After implementation, four reviewers (engine, networking, house style, tests) hunted defects
in the finished tree and one skeptic per finding tried to refute it against the decompiled
game. Six findings; two survived, four were refuted with two cheap hardenings recommended.

**Fixed:**
- **The dead pin did not survive respawn.** The snapshot shared a slot with the live entry
  and was served only while the character id was None — roughly the death screen, one or two
  cadences — then the respawn overwrote it. `DeathLedger` now keeps live and dead in separate
  stores, detects a death by the id going None OR changing between cadences, and serves the
  corpse under its old character id alongside the respawned player until `DeathGraceSeconds`
  elapses or the player disconnects. Tested through the whole story.
- **A failed `adminlist.txt` read revoked everyone instantly.** `SyncedList.Load` clears the
  list before it reads and swallows the exception, so a file open in an editor at reload
  reads as empty; every admin would have been demoted with an explicit revoke, bypassing the
  grace window that exists for exactly this. Demotions are now decided AFTER the peer loop —
  when the `IsAdmin` calls have already forced the reload — and skipped entirely while the
  list reads as empty with players online. The warning names the state; status shows it.

**Hardened on the refuted findings' advice:**
- The granting packet is built over a view clamped to the 64-entry wire cap (live first,
  deaths last), logged once; a packet-build failure degrades to "no grant this cadence"
  instead of aborting the cadence and the demotions it owns. (Vanilla hard-caps a server at
  10 players in `RPC_PeerInfo`, so the throw was unreachable; the shape is still better.)
- The relock guard also checks the applied map mode, so a `SetMapMode` that threw is retried
  next pass rather than latched away by the flag written first. The unlock direction does
  not check the mode, because vanilla holds it at None while the player is dead.
- The two recurring tick catches are capped at three log lines like every other error path.

**Refuted:** the unbounded-log claim (the catch fires once per flag flip, not twice a second);
the oversize-roster abort (unreachable under vanilla's 10-player cap); and the
write-then-apply latch as a live failure (no vanilla `SetMapMode` throw leaves a map visible).

## 6. Tests

A net10 harness compiles the SHIPPING `Grant.cs`, `RosterMerge.cs`, `RosterPacket.cs`,
`DeathLedger.cs` and `ModConfig.cs` against stubs. The ZPackage stub is a
BinaryWriter/BinaryReader with the real encodings (F10), so the packet round-trip is real
bytes through the real writer and reader. 143 assertions: the death ledger through death,
respawn, straddled respawn, second death, expiry, disconnect, grace zero and never-spawned;
config defaults and ranges (the interval ceiling stays under the grace floor;
the grace range sits inside what clients honour); grace clamping and freshness edges; every
`Grant.Reason`; the desired-flag truth table including the dropped-revoke case; the
tracker; packet round-trips (empty, null, UTF-8 names, dead flag, order), truncation,
version mismatch naming the side to update, non-finite grace and positions, malformed and
`int.MaxValue` counts, revoke-with-entries, oversize writes, exact byte size; merge
exclusions (self, duplicates against vanilla and within the roster, None ids), dead suffix,
order, nulls, and a poisoned-entry no-throw check.

Eight mutations were run — each fix removed in turn, including reverting the death snapshot
to the shared slot the review caught — and each broke its own tests.

## 7. Verification status and plan

- **Headless (done)**: on the minimal CairnTest dedicated server, the plugin loads on the
  server binary, three patches apply, the role line reports the admin list count, and no
  exception names the mod. (An `ArgumentNullException` from `ShieldDomeImageEffect.Awake` is
  vanilla headless noise, present without the mod.)
- **A screen (2026-09-06, partial)**: on a fresh dedicated no-map world (`VantageTest`,
  `-setkey nomap`) with a tester joining as admin from a Gale client, the vanilla minimap
  rendered with biome label and wind arrow; the server logged a one-entry roster to one
  admin every cadence; the client logged the grant with zero flag corrections; `raveneye
  status` answered. The explicit revoke then verified live: the tester removed their own ID
  from `adminlist.txt`, the server logged the demotion within SyncedList's 10 s re-check, the
  client relocked within one cadence and its status named the reason. `raveneye map off|on`
  verified in the same session. A second, non-admin account then joined and the admin saw its
  pin move on the admin map while that client showed no map — the pin path is verified live.
  0.1.0 shipped as an early release on that evidence. Still pending (as of 2026-09-06): the dead
  snapshot holding for its three minutes, and the listen-host case, which is the one most likely
  to be silently broken by vanilla's own `mapenabled_` pref. (Since then: the host granted as
  authority on its own no-map world was seen 2026-09-24; a host whose own character typed
  `nomap`, and the dead snapshot, are still open.)
