# RavenEye

A Valheim mod by **Raven Iron**. On a world with no map, the people running the server get
the vanilla map back — minimap, large map, their own exploration — with a vanilla player pin
for every player online, sharing or not, and a dead player's last position for a while.
Everyone else sees exactly what they saw before.

**Not** a HUD, an overlay, a player list, a tracker, or a cheat for players. It draws nothing
of its own: the game draws its own map for one more class of player, and its own pins for
more people. If a task seems to call for new UI, re-read this paragraph instead of building
it.

Sibling of Cairn (this repo's template), Undertow, FireFront and Ragnarok's Wrath, and bound
by the same house style. Ragnarok's Wrath's locked decisions forbid player-facing UI there,
which is why this is its own mod. Naming history, all on 2026-09-06: the working name
Heimdall collided with an existing Thunderstore mod (JJeweLin) and a design panel found it
off-register; the panel picked Vantage; the owner then chose **RavenEye**, spelled as one word
because that is what their logo says, and it was checked free on Hexium (searches "raven" and
"eye"), Thunderstore and Nexus that day. It sits beside The Raven's Call on the shelf and
shares no code or data with it. The test WORLD is still called VantageTest; it is only a world.

Design document — the reasoning, the critique panel's findings and every decision:
`docs/DESIGN.md`.

**Status (2026-09-06, 0.1.0): built, 143/143 off-game, eight load-bearing tests proven to
fail without their fix; reviewed by a four-reviewer adversarial pass (two real defects found
and fixed — see docs/DESIGN.md "Post-review fixes"); headless verified three times on the
CairnTest dedicated server (plugin boots on the server binary, three patches apply, the
admin list is read at the role line, the roster cadence runs with the gate open).
**SEEN LIVE 2026-09-06 (dedicated VantageTest server, no-map world, the owner as admin on a
Gale client):** the vanilla minimap rendered on a no-map world with biome label and wind
arrow; server log `roster: 1 entry (0 dead) sent to 1 admin(s)`; client log
`admin map GRANTED — server roster 0s ago, grace 60s` with zero flag corrections (the grant
landed before first spawn and vanilla's own UpdateNoMap call applied it via the postfix);
`raveneye status` works. The REVOKE is verified: the owner removed their own ID from
`adminlist.txt` while online; the server logged `is no longer an admin — map revoked`, the
client's status read `grant: no — the server revoked it 5s ago`, `Minimap mode=None`, one
revoke received; the unreadable-list guard correctly stayed out of it (one entry remained).
`raveneye map off` / `map on` verified too (corrections 1 and 2 of the session's 3). A second
account (TesTylass, no admin entry) then joined from the same laptop: the owner SAW THEIR PIN
MOVE on the admin map, and the other client showed no map. The owner declared 0.1.0 an EARLY
RELEASE on that evidence. NOT yet seen: the dead snapshot holding for three minutes, and the
listen-host case — see "What to verify"; both are stated as unverified in the README.**

---

## Commands

```powershell
.\tools\fetch-libs.ps1     # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1      # off-game logic tests (net10) — run before every commit
.\tools\package.ps1        # Release build + store zip in dist\ (writes manifest version from the csproj)
dotnet build RavenEye\RavenEye.csproj
```

To inspect a game member — signature, accessibility, or the actual body — decompile it:

```powershell
$m = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
ilspycmd -r $m $m\assembly_valheim.dll -t Minimap        # the REAL assembly: true accessibility
ilspycmd -r $m $m\publicized_assemblies\assembly_valheim_publicized.dll -t Minimap
```

Read the body; do not infer it from the shape. And do not `head`-truncate a member grep:
the first draft of this mod reflected into a private method because a truncated grep hid
the public `ZNet.IsAdmin(string)` wrapper three screens further down.

To test in-game: copy `RavenEye\bin\Debug\RavenEye.dll` into `<install>\BepInEx\plugins\`.
The owner's client runs through Gale (`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`);
dedicated test servers live under `C:\Users\donfr\ValheimServers\` (CairnTest on port 2466 is
the minimal one; see the RagnaroksWrath memory notes). Valheim locks the DLL while running.

---

## Layout

```
RavenEye/
  RavenEye.cs                plugin entry: config, Harmony, tick, boot line
  Config/ModConfig.cs       Server.* read on the server, Client.* read on a screen
  Core/Grant.cs             PURE: grant reason (explicit revoke, grace backstop), desired flag
  Core/RosterMerge.cs       PURE: append roster entries to vanilla's public-player list
  Core/DeathLedger.cs       PURE: live/dead stores; a corpse pin that survives the respawn
  Core/RavenEyeTick.cs       the single Update: server cadence, client CONVERGENCE
  Net/RosterPacket.cs       PURE: the wire format (versioned in the payload)
  Net/RosterSync.cs         server build+send+revoke (direct peer ZRpc), client register+receive
  Server/AdminGate.cs       the ONE method that names vanilla's admin API (public ZNet.IsAdmin)
  Patches/Patch_Game_UpdateNoMap.cs           corrects vanilla's cold recomputation for a granted admin
  Patches/Patch_ZNet_GetOtherPublicPlayers.cs the pins (append to vanilla's list)
  Patches/Patch_Terminal.cs                   `raveneye status | roster | map on|off`
tests/CoreTests/            net10 harness; compiles the REAL source against stubs
tools/                      fetch-libs, run-tests, package
libs/                       gitignored; populated by fetch-libs.ps1
docs/DESIGN.md              the reasoning, the critique, the decisions
```

---

## House style — non-negotiable (inherited; each rule came from a measured failure)

1. **Harmony: prefixes for behaviour (`Priority.Low`, honour `__runOriginal`) and
   RESULT-DECORATING postfixes at default priority where appending is the whole point.**
   Cede the final say. `Patch_ZNet_GetOtherPublicPlayers` is the canonical decorator.
   `Patch_Game_UpdateNoMap` is NOT — it corrects a global static after a void method — and is
   recorded below as a narrow, named exception on its own evidence. Never max-priority replace.
2. **No long-lived coroutines.** `RavenEyeTick` is one `Update`. Nothing else owns a timer.
3. **Cosmetics off the gameplay path.** Every patch body is its own try/catch, logging at most
   three times. `Game.UpdateNoMap` runs inside the global-key application loop; a throw there
   would drop every world modifier after it, so this is not decoration.
4. **Never patch `EnvMan`. Never touch materials or textures.** Not relevant here, still binding.
5. **Publicized assemblies are COMPILE-TIME ONLY.** The real assembly keeps its original
   accessibility and Mono refuses private access when the CALLING method is JIT-compiled.
   This mod touches NO private member. Every member it uses was checked public in the REAL
   assembly on 2026-09-06 (list in docs/DESIGN.md). Before touching a new one, check `real/`.

**Debugging discipline.** `raveneye status` states the grant's reason in words because "the
map did not appear" has eight causes with one symptom. Spend the first round-trip on that
command, not on a guess.

---

## Locked decisions — do not revisit without asking

| Decision | Answer |
|---|---|
| Where it lives | Its own mod. RW's scope forbids UI; Cairn's forbids maps. |
| What draws the map | **Vanilla.** No UI of ours, ever. We correct one flag and append to one list. |
| Who is an admin | **The server's `adminlist.txt`, via the PUBLIC `ZNet.IsAdmin(string hostName)`** — vanilla's own wrapper over `ListContainsId(m_adminList, host)`, the exact check it applies to remote commands, kicks and bans. `PlayerIsAdmin(PlatformUserID)` is NOT equivalent (one id form only). No reflection anywhere. |
| Admin check throws | **Nobody is granted**, error logged once naming the API. Never "everybody". |
| Transport | **Direct peer `ZRpc` (`peer.m_rpc.Invoke`), never `ZRoutedRpc`.** Routed RPCs are forwarded to whatever target/sender a client writes into the packet; a peer socket has one possible origin. The receiver is registered ONLY on a client's server socket (`ZNet.GetServerRPC()`), never on the server role. |
| Client → server messages | **None.** The client cannot ask. |
| The grant | **A demotion is an explicit revoke packet** (one interval). **Silence is a backstop**: `GraceSeconds` (60, clamped 10–120 by the client) after the last granting roster. Listen host is granted by construction while the SAME two server gates the builder uses are open. |
| Unlock seam | **`Game.m_noMap`, corrected by convergence** (`RavenEyeTick.ClientTick`, twice a second, direct write + conditional `SetMapMode`), plus a postfix on `Game.UpdateNoMap` for vanilla's own cold recomputations. Never edge-triggered: an edge that fires while the player is dead is an edge lost. Rationale: one writer, one reader (`Minimap.SetMapMode`), ~3 vanilla calls per session. |
| The character's `nomap` pref | **Respected**; RavenEye lifts the world's rule only. `raveneye map on` clears the pref, because that is what the words mean. |
| Gates | `Enabled` and `RevealWhenMapEnabled` gate the roster **BUILDER**, so a listen host reading the roster directly is bound by them. `RevealWhenMapEnabled` defaults **false**. |
| Death | Last known position served for `DeathGraceSeconds` (180), marked dead, snapshot from the last cadence that still had the character id; the live reference position is never served for a dead player. |
| Wire contents | **format version, server version, granted, grace, then per entry name, character ZDOID, position, dead flag — and nothing else.** No platform ids, no stats. Frozen. |
| Wire versioning | **Inside the payload**, RPC name stable (`com.raveniron.raveneye.roster`). A deliberate departure from the sibling mods' name-suffix convention: a suffix no-ops silently, indistinguishable from "server has no mod"; a payload version is a log line naming the side to update. |
| Terrain reveal (`ExploreAll`) | **Not done, not even as a command.** The game cannot un-explore; vanilla `devcommands` + `exploremap` exist and require admin already. |
| Console prefix | `raveneye` |
| GUID / namespace | `com.raveniron.raveneye` / `RavenIron.RavenEye` |

---

## Engine facts — Valheim 0.221.12, decompiled 2026-09-06

- `Game.m_noMap` (public static) has ONE writer (`Game.UpdateNoMap`) and ONE reader
  (`Minimap.SetMapMode`, which coerces the requested mode to None) in the whole assembly — the
  critique re-verified this over a full 602-type decompile. `Minimap.Update` calls
  `SetMapMode(Small)` every frame the mode is None, and runs `UpdateExplore` BEFORE that:
  exploration is recorded on no-map worlds.
- `Game.UpdateNoMap()` (public static) = world `NoMap` key OR `PlatformPrefs "mapenabled_<name>" == 0`
  (the `nomap` console command, keyed by character NAME), then `SetMapMode`. Two callers:
  first spawn (behind a one-shot `m_firstSpawn` — NOT per respawn) and `UpdateWorldRates` on
  global-key sync. It dereferences `Minimap.instance` unconditionally, and `Minimap.instance`
  exists on a dedicated server (ZoneSystem.Start calls UpdateWorldRates on every role).
- Two nomap-dependent gates besides the flag, both keyed on `Minimap.m_mode == None`: the
  chat ping distance (Chat.cs) and `Game.RPC_DiscoverLocationResponse` (Vegvisir turns the
  head instead of pinning). A granted admin gets the map-world behaviour of both. Intended.
- Player pins: `Minimap.UpdatePlayerPins` → `ZNet.GetOtherPublicPlayers(list)` every frame;
  rebuilds pins only when the COUNT changes, matches by INDEX, smooths only when the name
  matches. A share-toggle flip moves a player from our tail to vanilla's head (snap, not
  glide); two characters with one name defeat smoothing. Both happen in vanilla already.
- The server always knows every position: clients send `m_referencePosition` every 2 s
  (`SendServerSyncPlayerData` → `peer.m_refPos`) regardless of the share toggle, and push
  every changed owned ZDO to the server (`ZDOMan.CreateSyncList`, client branch), so the
  server holds every connected player's character ZDO. Vanilla's `SendPlayerList` omits the
  position when not shared.
- Death: `Player` calls `RequestRespawn(10f)`; `_RequestRespawn` sets the character id to
  `ZDOID.None` and the client tells the server (`RPC_CharacterID`); vanilla's own pins drop
  the player for that window too.
- `ZNet.IsAdmin(string)` is public: `ListContainsId(m_adminList, hostName)`, which accepts both
  the prefixed platform id and the bare Steam64. `m_adminList` (private `SyncedList`) exists
  for dedicated AND listen hosts. `SyncedList.Load` clears the list, latches the file's mtime,
  then reads, swallowing exceptions: a file open in an editor at reload reads as EMPTY until
  its mtime changes. `ZNet.GetAdminList()` (public) is that same live list on the server.
- `ZRoutedRpc.RPC_RoutedRPC`: the server forwards to `m_targetPeerID` as written by the
  sender; `m_senderPeerID` is taken from the packet; target 0 broadcasts. Unusable for
  anything an untrusted client must not forge. `ZRpc.Register` removes-then-adds
  (idempotent); `ZRpc.HandlePackage` silently ignores an unregistered method.
- Private at runtime, among members considered and avoided: `ZNet.m_peers`, `m_adminList`,
  `m_players`, `m_referencePosition`, `m_characterID`, `m_routedRpc`, `ListContainsId`,
  `ZRoutedRpc.m_id`, `GetServerPeerID`, `Minimap.m_playerPins`, `ZRpc.m_functions`.

---

## What to verify in-game (an admin's eyes; not yet done)

1. **LISTEN HOST FIRST.** Host a world, type `nomap` as host (this sets the world key AND the
   host's own character pref). `raveneye status` must say the grant is on as authority AND
   "THIS CHARACTER'S OWN MAP IS OFF". `raveneye map on` clears it; the map appears.
2. Admin client on a dedicated `nomap` world: log shows `admin map GRANTED — server roster
   …`; minimap appears; `M` opens the large map; `raveneye status` says `grant: YES` with a
   roster count and `receiver registered on this connection: yes`.
3. A second, non-admin player online: their pin appears on the admin's map and moves;
   `raveneye roster` lists them. Their own screen: no map, no pins, `raveneye status` says
   "no roster has arrived since this join".
4. Kill the second player: within one cadence their pin stays where they fell as
   "Name (dead)"; after respawn it follows them again.
5. Remove the admin from `adminlist.txt` while online: within ~12 s (SyncedList's 10 s
   re-check + one interval) the server logs `is no longer an admin — map revoked`, the
   client logs `not granted — the server revoked it`, the map hides.
6. `raveneye map off` hides it within half a second; `raveneye map on` brings it back.
7. Die and respawn with the grant: the map returns.

---

## Working agreement

- **Run `.\tools\run-tests.ps1` before every commit.**
- **Prove a new test fails without its fix.** Eight mutations were run on 2026-09-06 (self
  exclusion, count bound, stale floor, revoke-with-entries, non-finite position,
  revoke-vs-never, disconnected, and the shared-slot death snapshot the review caught); each
  broke its own tests.
- **Demotions are decided after the peer loop, never inside it.** The `IsAdmin` calls force
  vanilla's `SyncedList` reload; only after them does the admin-list count mean anything, and
  zero-with-players-online means "unreadable", not "nobody". Keep it that way.
- **A clean build proves nothing about member access.** Headless verified; a screen is next.
- **Ask before changing anything in the locked-decisions table.**
