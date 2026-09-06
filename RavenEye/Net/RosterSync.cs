using System;
using System.Collections.Generic;
using RavenIron.RavenEye.Core;
using RavenIron.RavenEye.Server;
using UnityEngine;

namespace RavenIron.RavenEye.Net
{
    /// <summary>
    /// Server → admin client: where everyone is.
    ///
    /// DIRECT PEER RPC, NOT ROUTED. Vanilla's routed RPCs are forwarded by the server to
    /// whatever `m_targetPeerID` the sender wrote, with the sender id taken from the packet
    /// (ZRoutedRpc.RPC_RoutedRPC, decompile-verified 2026-09-06) — so any client can address
    /// any other client and claim to be the server. A message on the peer's own `ZRpc`
    /// socket has exactly one possible origin: the machine at the other end, and for a
    /// client that is the server. Nothing to verify, nothing to forge. `ZRpc.HandlePackage`
    /// ignores a method it has no handler for, so an admin WITHOUT the mod receives silence,
    /// not a disconnect.
    ///
    /// ABSOLUTE SNAPSHOTS, ON CADENCE, UNCONDITIONAL. Every send carries every online player,
    /// whether or not anything changed. A dropped packet heals on the next one; a joining
    /// admin is right within one interval. A DEMOTION is one explicit revoke packet; silence
    /// is only the backstop — see <see cref="Grant"/>.
    ///
    /// The server never registers a receiver. There is no client → server message in this
    /// mod; a client cannot ask for anything.
    /// </summary>
    public static class RosterSync
    {
        /// <summary>
        /// No version suffix here, deliberately: the format version travels INSIDE the packet
        /// (<see cref="RosterPacket.FormatVersion"/>) so a mismatch is a log line naming the
        /// side to update, not the same silence as "the server has no mod".
        /// </summary>
        public const string RpcName = "com.raveniron.raveneye.roster";

        // ---- client side ------------------------------------------------------------------

        private static readonly List<RosterEntry> _cache = new List<RosterEntry>(16);
        private static readonly List<RosterEntry> _scratch = new List<RosterEntry>(16);
        private static ZRpc _registeredOn;

        /// <summary><see cref="Time.realtimeSinceStartup"/> of the last GRANTING roster, or <see cref="Grant.Never"/>.</summary>
        public static float LastReceipt { get; private set; } = Grant.Never;

        /// <summary>When the server last said "not granted", or Never.</summary>
        public static float LastRevoke { get; private set; } = Grant.Never;

        /// <summary>The grace window the last granting roster carried (before clamping).</summary>
        public static float LastGrace { get; private set; }

        public static string ServerVersion { get; private set; } = "";
        public static int PacketsReceived { get; private set; }
        public static int RevokesReceived { get; private set; }
        public static int PacketsRejected { get; private set; }
        public static string LastRejectReason { get; private set; } = "";

        /// <summary>Set by the pin postfix: how many entries it appended on the last frame.</summary>
        public static int LastAppended { get; set; }

        // ---- server side ------------------------------------------------------------------

        private const long HostKey = long.MinValue;

        private static readonly List<RosterEntry> _roster = new List<RosterEntry>(16);
        private static readonly List<RosterEntry> _wire = new List<RosterEntry>(16);
        private static readonly List<ZNetPeer> _peerScratch = new List<ZNetPeer>(16);
        private static readonly List<ZNetPeer> _demoteScratch = new List<ZNetPeer>(4);
        private static readonly HashSet<long> _grantedPeers = new HashSet<long>();
        private static readonly List<long> _uidScratch = new List<long>(16);
        private static readonly List<long> _presentScratch = new List<long>(16);
        private static readonly DeathLedger _ledger = new DeathLedger();
        private static bool _emptyAdminListWarned;
        private static bool _overflowLogged;
        private static int _packetFailuresLogged;

        public static float LastSendTime { get; private set; } = Grant.Never;
        public static int LastSendAdmins { get; private set; }
        public static int LastRosterSize { get; private set; }
        public static int DeadSnapshots { get; private set; }
        public static int LastDropped { get; private set; }
        public static int GrantedPeerCount => _grantedPeers.Count;

        /// <summary>Why the last cadence built nothing, or "" when it built a roster.</summary>
        public static string ClosedGate { get; private set; } = "";

        /// <summary>True while the admin list reads as empty with players online — see <see cref="WarnIfAdminListEmpty"/>.</summary>
        public static bool AdminListUnreadable { get; private set; }

        /// <summary>Called when ZNet goes away: nothing from one world may leak into the next.</summary>
        public static void ResetSession()
        {
            _cache.Clear();
            _scratch.Clear();
            _registeredOn = null;
            _roster.Clear();
            _wire.Clear();
            _peerScratch.Clear();
            _demoteScratch.Clear();
            _grantedPeers.Clear();
            _uidScratch.Clear();
            _presentScratch.Clear();
            _ledger.Clear();
            _emptyAdminListWarned = false;
            _overflowLogged = false;
            _packetFailuresLogged = 0;
            LastReceipt = Grant.Never;
            LastRevoke = Grant.Never;
            LastGrace = 0f;
            ServerVersion = "";
            PacketsReceived = 0;
            RevokesReceived = 0;
            PacketsRejected = 0;
            LastRejectReason = "";
            LastAppended = 0;
            LastSendTime = Grant.Never;
            LastSendAdmins = 0;
            LastRosterSize = 0;
            DeadSnapshots = 0;
            LastDropped = 0;
            ClosedGate = "";
            AdminListUnreadable = false;
        }

        /// <summary>
        /// The roster this machine acts on. The authority reads what it built for its last
        /// send (a listen host must not wait on a round-trip to itself); a client reads the
        /// synced cache.
        /// </summary>
        public static IList<RosterEntry> Current(ZNet znet)
        {
            if (znet != null && znet.IsServer()) return _roster;
            return _cache;
        }

        // ---- client: registration and receipt --------------------------------------------

        /// <summary>
        /// Register the receiver on the server's socket — a client has exactly one. Called
        /// every frame from the tick; a new connection hands us a new ZRpc and we register
        /// again. `ZRpc.Register` removes before it adds, so this is safe to repeat.
        /// </summary>
        public static void EnsureRegistered(ZNet znet)
        {
            if (znet == null || znet.IsServer()) return;

            ZRpc rpc;
            try { rpc = znet.GetServerRPC(); }
            catch { return; }
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;

            try
            {
                rpc.Register<ZPackage>(RpcName, RPC_Roster);
                _registeredOn = rpc;
            }
            catch (Exception ex)
            {
                RavenEye.Log.LogWarning($"RosterSync: register on the server socket failed: {ex.Message}");
                _registeredOn = rpc;   // do not retry every frame against a broken socket
            }
        }

        /// <summary>Is the receiver registered on the socket this client is using right now?</summary>
        public static bool IsRegisteredOnCurrentConnection(ZNet znet)
        {
            if (znet == null || znet.IsServer()) return false;
            try { return _registeredOn != null && ReferenceEquals(_registeredOn, znet.GetServerRPC()); }
            catch { return false; }
        }

        /// <summary>
        /// Receiver. The authority refuses it: its roster is the one it built, and on a
        /// listen host nothing should arrive here anyway.
        /// </summary>
        private static void RPC_Roster(ZRpc rpc, ZPackage pkg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer()) return;

            try
            {
                RosterHeader header = RosterPacket.Read(pkg, _scratch);
                ServerVersion = header.ServerVersion ?? "";

                if (header.Granted)
                {
                    _cache.Clear();
                    _cache.AddRange(_scratch);
                    LastGrace = header.GraceSeconds;
                    LastReceipt = Time.realtimeSinceStartup;
                    PacketsReceived++;
                }
                else
                {
                    // Explicit revoke: the grant ends now, not after the grace window.
                    _cache.Clear();
                    LastReceipt = Grant.Never;
                    LastRevoke = Time.realtimeSinceStartup;
                    RevokesReceived++;
                }
            }
            catch (Exception ex)
            {
                // The previous roster stands; the next good packet is one interval away. A
                // format mismatch says so by name, once, because it is the one rejection an
                // admin can fix.
                PacketsRejected++;
                string reason = $"{ex.GetType().Name}: {ex.Message}";
                if (PacketsRejected <= 3 || reason != LastRejectReason)
                    RavenEye.Log.LogWarning($"RosterSync: rejected a roster packet ({reason}).");
                LastRejectReason = reason;
            }
        }

        // ---- server: build and send ------------------------------------------------------

        /// <summary>
        /// Does the world call for a roster at all? Always on a no-map world; on a map world
        /// only when the owner asked to see hidden players too.
        /// </summary>
        public static bool RevealActive(bool revealWhenMapEnabled)
        {
            if (revealWhenMapEnabled) return true;
            ZoneSystem zs = ZoneSystem.instance;
            return zs != null && zs.GetGlobalKey(GlobalKeys.NoMap);
        }

        /// <summary>
        /// Every online, spawned player, then — for <paramref name="deathGraceSeconds"/> —
        /// the last known position of anyone who has died, kept across their respawn.
        ///
        /// Live positions come from the character's ZDO when the server holds it — which it
        /// does for every connected player, because a client pushes every owned ZDO that
        /// changed (`ZDOMan.CreateSyncList`, client branch, decompile-verified 2026-09-06) —
        /// and otherwise from the peer's reference position, the 2 s-cadence value vanilla
        /// draws its own shared pins from. Both are reported by the watched client; the ZDO
        /// is at least where the shared world puts the character.
        ///
        /// Live entries first, deaths last: the wire clamp below drops deaths before players.
        /// On a listen host the host is not a peer, so it is added by hand from public surfaces.
        /// </summary>
        public static void BuildRoster(ZNet znet, List<RosterEntry> into, float now, float deathGraceSeconds)
        {
            into.Clear();
            _presentScratch.Clear();

            if (!znet.IsDedicated())
            {
                ZDOID self = znet.LocalPlayerCharacterID;
                Observe(HostKey, self, HostPlayerName(), znet.GetReferencePosition(), into, now);
            }

            List<ZNetPeer> peers = znet.GetPeers();
            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    ZNetPeer peer = peers[i];
                    if (peer == null || !peer.IsReady()) continue;
                    Observe(peer.m_uid, peer.m_characterID, peer.m_playerName ?? "", peer.m_refPos, into, now);
                }
            }

            // Whoever is no longer connected takes their entries with them.
            _ledger.Retain(_presentScratch);

            DeadSnapshots = _ledger.Emit(into, now, deathGraceSeconds);
            LastRosterSize = into.Count;
        }

        private static void Observe(long key, ZDOID id, string name, Vector3 refPos, List<RosterEntry> into, float now)
        {
            _presentScratch.Add(key);

            if (id.IsNone())
            {
                _ledger.Observe(key, false, default(RosterEntry), now);
                return;
            }

            var entry = new RosterEntry { Name = name, Id = id, Position = PositionOf(id, refPos), Dead = false };
            into.Add(entry);
            _ledger.Observe(key, true, entry, now);
        }

        private static Vector3 PositionOf(ZDOID id, Vector3 fallback)
        {
            try
            {
                ZDOMan man = ZDOMan.instance;
                if (man != null)
                {
                    ZDO zdo = man.GetZDO(id);
                    if (zdo != null) return zdo.GetPosition();
                }
            }
            catch { /* the reference position is always a valid answer */ }
            return fallback;
        }

        private static string HostPlayerName()
        {
            try { return Game.instance?.GetPlayerProfile()?.GetName() ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// One cadence: decide whether the world calls for a roster, build it, send it to
        /// every admin peer, and send one revoke to anyone who just stopped being one —
        /// unless the admin list itself is unreadable, in which case nobody is demoted.
        /// </summary>
        /// <returns>Admins reached.</returns>
        public static int SendToAdmins(ZNet znet, bool enabled, float graceSeconds, float deathGraceSeconds,
                                       bool revealWhenMapEnabled, bool verbose)
        {
            float now = Time.realtimeSinceStartup;
            LastSendTime = now;

            List<ZNetPeer> live = znet.GetPeers();
            _peerScratch.Clear();
            if (live != null) _peerScratch.AddRange(live);   // a copy: sending must not iterate the live list

            // The gates live HERE, in the builder, so a listen host reading the roster
            // directly is bound by exactly the same switches as a remote admin.
            string closed = !enabled ? "Server.Enabled is off"
                          : !RevealActive(revealWhenMapEnabled) ? "the world has a map and RevealWhenMapEnabled is off"
                          : "";
            if (closed.Length > 0)
            {
                if (ClosedGate != closed) RavenEye.Log.LogInfo($"roster: gate closed — {closed}.");
                ClosedGate = closed;
                _roster.Clear();
                _ledger.Clear();
                LastRosterSize = 0;
                DeadSnapshots = 0;
                RevokeEveryone(graceSeconds, "gate closed");
                LastSendAdmins = 0;
                return 0;
            }
            if (ClosedGate.Length > 0) RavenEye.Log.LogInfo("roster: gate open again.");
            ClosedGate = "";

            BuildRoster(znet, _roster, now, deathGraceSeconds);

            ZPackage grantPkg = null;
            bool packetFailed = false;
            int sent = 0;
            _uidScratch.Clear();
            _demoteScratch.Clear();

            for (int i = 0; i < _peerScratch.Count; i++)
            {
                ZNetPeer peer = _peerScratch[i];
                if (peer == null || !peer.IsReady()) continue;
                _uidScratch.Add(peer.m_uid);

                if (!AdminGate.IsAdmin(znet, peer))
                {
                    // Decided AFTER the loop: a demotion is only believed once we know the
                    // list itself was readable this cadence.
                    if (_grantedPeers.Contains(peer.m_uid)) _demoteScratch.Add(peer);
                    continue;
                }

                if (grantPkg == null && !packetFailed)
                {
                    try { grantPkg = BuildGrantPacket(graceSeconds); }
                    catch (Exception ex)
                    {
                        // Degrade to "no grant this cadence" rather than aborting the cadence
                        // and the demotions it owns. Silence then lapses within the grace.
                        packetFailed = true;
                        if (_packetFailuresLogged++ < 3)
                            RavenEye.Log.LogWarning($"roster: could not build the packet ({ex.GetType().Name}: {ex.Message}); no grant this cadence.");
                    }
                }
                if (grantPkg == null) continue;

                try
                {
                    peer.m_rpc.Invoke(RpcName, grantPkg);
                    sent++;
                }
                catch (Exception ex)
                {
                    RavenEye.Log.LogWarning($"roster: send to {Describe(peer)} failed: {ex.Message}");
                    continue;
                }

                if (_grantedPeers.Add(peer.m_uid))
                    RavenEye.Log.LogInfo($"roster: {Describe(peer)} is an admin — map granted.");
            }

            // The IsAdmin calls above forced SyncedList's reload, so this count is current.
            // Zero entries with players online is vanilla's silent failure mode (a file open
            // in an editor at reload time), not a mass de-admin: demote nobody, warn once, and
            // let the clients' own grace lapse if it turns out to be real.
            int admins = AdminGate.AdminListCount(znet);
            AdminListUnreadable = admins <= 0 && _uidScratch.Count > 0;
            WarnIfAdminListEmpty(admins, _uidScratch.Count);

            if (!AdminListUnreadable)
            {
                for (int i = 0; i < _demoteScratch.Count; i++)
                {
                    ZNetPeer peer = _demoteScratch[i];
                    _grantedPeers.Remove(peer.m_uid);
                    SendRevoke(peer, graceSeconds);
                    RavenEye.Log.LogInfo($"roster: {Describe(peer)} is no longer an admin — map revoked.");
                }
            }

            // Peers that left take their grant with them; otherwise a reconnect under a new
            // uid would log a fresh grant while the old one lingered in the set.
            if (_grantedPeers.Count > 0)
                _grantedPeers.RemoveWhere(uid => !_uidScratch.Contains(uid));

            LastSendAdmins = sent;
            if (verbose)
                RavenEye.Log.LogInfo($"roster: {_roster.Count} entr{(_roster.Count == 1 ? "y" : "ies")} ({DeadSnapshots} dead) sent to {sent} admin(s).");
            return sent;
        }

        /// <summary>
        /// The granting packet, over a view of the roster clamped to the wire's cap — live
        /// players first, deaths last, so a crowd drops corpses before people. Logged once.
        /// `Current()` keeps the full roster for the host.
        /// </summary>
        private static ZPackage BuildGrantPacket(float graceSeconds)
        {
            _wire.Clear();
            int take = Math.Min(_roster.Count, RosterPacket.MaxEntries);
            for (int i = 0; i < take; i++) _wire.Add(_roster[i]);

            LastDropped = _roster.Count - take;
            if (LastDropped > 0 && !_overflowLogged)
            {
                _overflowLogged = true;
                RavenEye.Log.LogWarning($"roster: {_roster.Count} entries exceed the wire cap of {RosterPacket.MaxEntries}; the first {take} are sent (deaths dropped first). Logged once.");
            }

            var pkg = new ZPackage();
            RosterPacket.Write(pkg,
                new RosterHeader { ServerVersion = RavenEye.PluginVersion, Granted = true, GraceSeconds = graceSeconds },
                _wire);
            return pkg;
        }

        private static void RevokeEveryone(float graceSeconds, string why)
        {
            if (_grantedPeers.Count == 0) return;
            for (int i = 0; i < _peerScratch.Count; i++)
            {
                ZNetPeer peer = _peerScratch[i];
                if (peer == null || !peer.IsReady() || !_grantedPeers.Contains(peer.m_uid)) continue;
                SendRevoke(peer, graceSeconds);
                RavenEye.Log.LogInfo($"roster: {Describe(peer)} — map revoked ({why}).");
            }
            _grantedPeers.Clear();
        }

        private static void SendRevoke(ZNetPeer peer, float graceSeconds)
        {
            try
            {
                var pkg = new ZPackage();
                RosterPacket.Write(pkg,
                    new RosterHeader { ServerVersion = RavenEye.PluginVersion, Granted = false, GraceSeconds = graceSeconds },
                    null);
                peer.m_rpc.Invoke(RpcName, pkg);
            }
            catch (Exception ex)
            {
                // Silence revokes too, after the grace window; this only made it faster.
                RavenEye.Log.LogWarning($"roster: revoke to {Describe(peer)} failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Vanilla's `SyncedList.Load` clears the list, latches the file's mtime, then reads
        /// — and swallows any exception. A file that is open in an editor at reload time
        /// therefore reads as EMPTY until its timestamp changes again, with no vanilla log
        /// line. `IsAdmin` is what polls it every 10 s, so this mod is what exposes the
        /// window; this is the line that names it, and the caller declines to demote on it.
        /// </summary>
        private static void WarnIfAdminListEmpty(int admins, int peerCount)
        {
            if (admins <= 0 && peerCount > 0)
            {
                if (_emptyAdminListWarned) return;
                _emptyAdminListWarned = true;
                RavenEye.Log.LogWarning(
                    $"roster: adminlist.txt reads as EMPTY while {peerCount} player(s) are online — no grants, and no " +
                    "revokes either (this is what a failed read looks like, not a de-admin). If the file is not " +
                    "actually empty, the game failed to read it (open in an editor?) and will only retry when its " +
                    "timestamp changes: re-save the file. Granted admins keep their map for the grace window.");
            }
            else if (admins > 0 && _emptyAdminListWarned)
            {
                _emptyAdminListWarned = false;
                RavenEye.Log.LogInfo($"roster: adminlist.txt reads {admins} entr{(admins == 1 ? "y" : "ies")} again.");
            }
        }

        private static string Describe(ZNetPeer peer)
        {
            string host;
            try { host = peer.m_socket?.GetHostName() ?? "?"; } catch { host = "?"; }
            return $"{peer.m_playerName} ({host})";
        }
    }
}
