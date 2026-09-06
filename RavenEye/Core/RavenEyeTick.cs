using System;
using RavenIron.RavenEye.Config;
using RavenIron.RavenEye.Net;
using RavenIron.RavenEye.Server;
using UnityEngine;

namespace RavenIron.RavenEye.Core
{
    /// <summary>
    /// The one place anything in this mod is driven from: a plain Update, no coroutines
    /// (house style rule 2 — every long-lived coroutine in this studio's lineage grew the
    /// same `continue`-past-`yield` hard-lock, and one reached production).
    ///
    /// Server branch: every <see cref="ModConfig.IntervalSeconds"/>, send the roster to the
    /// admins. Client branch: CONVERGE. Twice a second, compute what `Game.m_noMap` should
    /// be and, if the live flag or the applied map mode disagrees and there is a player to
    /// apply it to, correct it. Not edge-triggered: the critique showed that a revoke landing
    /// while the player is dead (no local player to apply it to) would otherwise be consumed
    /// and lost, leaving a de-admined player a map for the rest of the session.
    /// </summary>
    public class RavenEyeTick : MonoBehaviour
    {
        private const float ConvergeEvery = 0.5f;

        private readonly GrantTracker _grant = new GrantTracker();
        private float _sendAccumulator;
        private float _convergeAccumulator;
        private bool _hadWorld;
        private bool _roleLogged;
        private int _reapplies;
        private int _sendFailuresLogged;
        private int _convergeFailuresLogged;

        /// <summary>The grant as of the last evaluation — for the console, not for decisions.</summary>
        public static bool GrantedNow { get; private set; }

        /// <summary>How many times the client had to correct the flag outside vanilla's own calls.</summary>
        public static int Reapplies { get; private set; }

        /// <summary>Human-readable role, for the console and the role line.</summary>
        public static string Role()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return "no world";
            if (znet.IsDedicated()) return "dedicated server";
            if (znet.IsServer()) return "listen host";
            return "client";
        }

        /// <summary>
        /// Is this process, right now, on the authority side of the feature: a server (either
        /// kind) with the feature on and the world calling for a roster? The SAME two gates
        /// the roster builder applies, so a listen host cannot bypass its own switches.
        /// </summary>
        public static bool IsAuthorityGranting(ZNet znet)
        {
            return znet != null
                && znet.IsServer()
                && ModConfig.ServerEnabled.Value
                && RosterSync.RevealActive(ModConfig.RevealWhenMapEnabled.Value);
        }

        /// <summary>The decision with its reason — the status line prints this in words.</summary>
        public static Grant.Reason ExplainGrant(ZNet znet)
        {
            if (znet == null) return Grant.Reason.NotConnected;

            bool authority = IsAuthorityGranting(znet);
            bool connected = znet.IsServer() || znet.GetServerPeer() != null;

            return Grant.Explain(
                ModConfig.ShowMap.Value,
                RavenEye.HasRenderer,
                authority,
                connected,
                Time.realtimeSinceStartup,
                RosterSync.LastReceipt,
                RosterSync.LastRevoke,
                RosterSync.LastGrace);
        }

        /// <summary>
        /// THE decision, evaluated fresh wherever it is needed — the two patches call this
        /// rather than reading a per-frame cache, so a roster that lands in the same frame as
        /// the first spawn is not missed by an Update-order accident.
        /// </summary>
        public static bool EvaluateGrant(ZNet znet) => Grant.IsGranted(ExplainGrant(znet));

        /// <summary>The character's own vanilla `nomap` opt-out, keyed by character NAME.</summary>
        public static bool PersonalOptOut(Player local)
        {
            if (local == null) return false;
            return PlatformPrefs.GetFloat(PersonalKey(local), 1f) == 0f;
        }

        public static string PersonalKey(Player local) => "mapenabled_" + local.GetPlayerName();

        private void Update()
        {
            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                if (_hadWorld)
                {
                    // Back to the main menu: nothing from this world may leak into the next.
                    _hadWorld = false;
                    _roleLogged = false;
                    _sendAccumulator = 0f;
                    _convergeAccumulator = 0f;
                    _grant.Reset();
                    GrantedNow = false;
                    RosterSync.ResetSession();
                }
                return;
            }
            _hadWorld = true;

            if (!_roleLogged)
            {
                _roleLogged = true;

                // Read the admin list NOW, with no peers needed, so a headless run with nobody
                // online still proves the one surface this mod depends on is where we think.
                string gate = znet.IsServer()
                    ? $"admin list entries={AdminGate.AdminListCount(znet)}"
                    : "admin list is the server's job";

                RavenEye.Log.LogInfo(
                    $"RavenEye online — role={Role()}, renderer={RavenEye.HasRenderer}, " +
                    $"server feature={(ModConfig.ServerEnabled.Value ? "on" : "off")}, " +
                    $"interval={ModConfig.IntervalSeconds.Value:0.##}s, grace={ModConfig.GraceSeconds.Value:0}s, " +
                    $"deathGrace={ModConfig.DeathGraceSeconds.Value:0}s, " +
                    $"revealWhenMapEnabled={ModConfig.RevealWhenMapEnabled.Value}, " +
                    $"client showMap={ModConfig.ShowMap.Value}, {gate}.");
            }

            if (znet.IsServer())
            {
                ServerTick(znet, Time.deltaTime);
            }
            else
            {
                RosterSync.EnsureRegistered(znet);
            }

            if (RavenEye.HasRenderer)
            {
                ClientTick(znet, Time.deltaTime);
            }
        }

        private void ServerTick(ZNet znet, float dt)
        {
            _sendAccumulator += dt;
            float interval = Mathf.Max(0.5f, ModConfig.IntervalSeconds.Value);
            if (_sendAccumulator < interval) return;
            _sendAccumulator = 0f;

            try
            {
                // Runs even when the feature is off, so that switching it off mid-session
                // revokes everyone it had granted rather than leaving them until silence does.
                RosterSync.SendToAdmins(znet,
                    ModConfig.ServerEnabled.Value,
                    ModConfig.GraceSeconds.Value,
                    ModConfig.DeathGraceSeconds.Value,
                    ModConfig.RevealWhenMapEnabled.Value,
                    ModConfig.VerboseLogging.Value);
            }
            catch (Exception ex)
            {
                if (_sendFailuresLogged++ < 3)
                    RavenEye.Log.LogWarning($"roster send failed ({ex.GetType().Name}: {ex.Message}); silence revokes within the grace window.");
            }
        }

        private void ClientTick(ZNet znet, float dt)
        {
            Grant.Reason reason = ExplainGrant(znet);
            bool granted = Grant.IsGranted(reason);
            GrantedNow = granted;

            bool changed = _grant.Update(granted);
            if (changed)
                RavenEye.Log.LogInfo(granted ? $"admin map GRANTED — {Words(reason)}." : $"admin map not granted — {Words(reason)}.");

            // Converge, on a change or on the half-second — never per frame, because the
            // personal opt-out is a PlatformPrefs read.
            _convergeAccumulator += dt;
            if (!changed && _convergeAccumulator < ConvergeEvery) return;
            _convergeAccumulator = 0f;

            Player local = Player.m_localPlayer;
            Minimap map = Minimap.instance;
            if (local == null || map == null) return;   // nothing to apply to yet; try again next half second

            try
            {
                bool worldNoMap = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoMap);
                bool desired = Grant.DesiredNoMap(granted, worldNoMap, PersonalOptOut(local));

                // "Applied" means the flag agrees AND, for a relock, the map is actually
                // hidden — so a SetMapMode that threw is seen as outstanding work next pass
                // rather than latched away by the flag we wrote first. The unlock direction
                // relies on vanilla's own Update (mode None → Small every frame) instead of
                // checking the mode, because vanilla holds the mode at None while the player
                // is dead and a check there would fight it twice a second.
                bool applied = Game.m_noMap == desired && (!desired || map.m_mode == Minimap.MapMode.None);
                if (applied) return;

                // Write the flag directly rather than calling Game.UpdateNoMap(): that would
                // recurse through our own postfix and force SetMapMode(Small) unconditionally,
                // yanking an open large map down to the corner on every correction.
                Game.m_noMap = desired;
                if (!desired)
                {
                    // Unlock: vanilla's Update would do this next frame anyway; doing it now
                    // avoids a one-frame flash.
                    if (map.m_mode == Minimap.MapMode.None) map.SetMapMode(Minimap.MapMode.Small);
                }
                else
                {
                    // Relock: hide now. Vanilla only calls SetMapMode when the mode is None,
                    // on death, or on the player's own toggle, so a visible map would otherwise
                    // stay visible until one of those.
                    map.SetMapMode(Minimap.MapMode.None);
                }

                Reapplies = ++_reapplies;
                if (_reapplies <= 5 || _reapplies % 100 == 0)
                    RavenEye.Log.LogInfo($"map flag corrected (noMap {(!desired)} → {desired}, {Words(reason)}); corrections so far: {_reapplies}.");
            }
            catch (Exception ex)
            {
                if (_convergeFailuresLogged++ < 3)
                    RavenEye.Log.LogWarning($"map convergence threw ({ex.GetType().Name}: {ex.Message}); will retry.");
            }
        }

        /// <summary>The reason, in words an admin can act on.</summary>
        public static string Words(Grant.Reason r)
        {
            switch (r)
            {
                case Grant.Reason.Granted:
                    return $"server roster {Time.realtimeSinceStartup - RosterSync.LastReceipt:0.#}s ago, grace {Grant.EffectiveGrace(RosterSync.LastGrace):0}s";
                case Grant.Reason.GrantedAsAuthority:
                    return "this is the host and the server gates are open";
                case Grant.Reason.ClientOff:
                    return "Client.ShowMap is off (`raveneye map on`)";
                case Grant.Reason.NoRenderer:
                    return "no renderer (headless)";
                case Grant.Reason.NotConnected:
                    return "not connected to a server";
                case Grant.Reason.Revoked:
                    return $"the server revoked it {Time.realtimeSinceStartup - RosterSync.LastRevoke:0}s ago (removed from adminlist.txt, or the server's gate closed)";
                case Grant.Reason.NeverReceived:
                    return "no roster has arrived since this join (not on the server's adminlist.txt, or the server is not running RavenEye)";
                case Grant.Reason.Stale:
                    return $"stale — last roster {Time.realtimeSinceStartup - RosterSync.LastReceipt:0}s ago, past the {Grant.EffectiveGrace(RosterSync.LastGrace):0}s grace (server stalled or stopped)";
                default:
                    return r.ToString();
            }
        }
    }
}
