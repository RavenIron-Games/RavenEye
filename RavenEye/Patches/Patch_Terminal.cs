using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using RavenIron.RavenEye.Config;
using RavenIron.RavenEye.Core;
using RavenIron.RavenEye.Net;
using RavenIron.RavenEye.Server;
using UnityEngine;

namespace RavenIron.RavenEye.Patches
{
    /// <summary>
    /// The `raveneye` console. Registered from an InitTerminal postfix; the ConsoleCommand
    /// constructor assigns into Terminal's command map by lowered name, so re-registration
    /// per terminal is harmless (Cairn, decompile-verified 2026-09-01; unchanged in 0.221.12).
    ///
    /// The instrument this mod cannot do without. "The map did not appear" has at least eight
    /// causes with one symptom — not on the adminlist, server without the mod, server gate
    /// closed, client toggle off, the character's own `nomap` opt-out, a stale roster, an
    /// explicit revoke, a version mismatch — and a boolean collapses them all. Every line here
    /// names the SOURCE of its value, because a roster of zero entries is what an empty server
    /// looks like and also what a never-received roster looks like.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_RavenEye
    {
        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("raveneye",
                    "RavenEye: status | roster | map on|off", Run);
            }
            catch (Exception ex)
            {
                RavenEye.Log.LogWarning($"raveneye console: registration failed: {ex.Message}");
            }
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "status": Status(args); return;
                    case "roster": Roster(args); return;
                    case "map":    Map(args); return;
                    default:       Help(args); return;
                }
            }
            catch (Exception ex)
            {
                Say(args, "raveneye: " + ex.Message);
                RavenEye.Log.LogWarning($"raveneye console threw: {ex}");
            }
        }

        private static void Help(Terminal.ConsoleEventArgs args)
        {
            Say(args, "raveneye status      — role, grant, and in words why the map is or is not showing");
            Say(args, "raveneye roster      — every player the roster carries (admins and hosts)");
            Say(args, "raveneye map on|off  — use, or refuse, the map the server grants this client");
        }

        private static void Status(Terminal.ConsoleEventArgs args)
        {
            ZNet znet = ZNet.instance;
            Say(args, $"RavenEye v{RavenEye.PluginVersion} — role={RavenEyeTick.Role()}, renderer={(RavenEye.HasRenderer ? "yes" : "no")}");
            if (znet == null) { Say(args, "  no world loaded."); return; }

            bool worldNoMap = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoMap);
            Say(args, $"  world NoMap key: {(worldNoMap ? "set (no-map world)" : "not set (this world has a map)")}");

            if (znet.IsServer())
            {
                Say(args, $"  server: Enabled={ModConfig.ServerEnabled.Value}, interval {F(ModConfig.IntervalSeconds.Value, "0.##")}s, " +
                          $"grace {F(ModConfig.GraceSeconds.Value, "0")}s, deathGrace {F(ModConfig.DeathGraceSeconds.Value, "0")}s, " +
                          $"RevealWhenMapEnabled={ModConfig.RevealWhenMapEnabled.Value}");
                Say(args, RosterSync.ClosedGate.Length > 0
                    ? $"  gate: CLOSED — {RosterSync.ClosedGate}"
                    : "  gate: open");

                int admins = AdminGate.AdminListCount(znet);
                Say(args, $"  adminlist.txt entries: {admins}" +
                          (RosterSync.AdminListUnreadable
                              ? "   <- reads as EMPTY with players online: treated as a failed read, nobody is demoted; re-save the file if it is not actually empty"
                              : ""));
                if (RosterSync.LastDropped > 0)
                    Say(args, $"  wire cap: {RosterSync.LastDropped} entr{(RosterSync.LastDropped == 1 ? "y" : "ies")} over the {RosterPacket.MaxEntries}-entry cap were not sent (deaths first)");

                if (RosterSync.LastSendTime < 0f)
                    Say(args, "  roster: never built yet (first cadence pending)");
                else if (RosterSync.ClosedGate.Length > 0)
                    Say(args, $"  roster: none — gate closed, last cadence {Age(RosterSync.LastSendTime)} ago");
                else
                    Say(args, $"  roster: {RosterSync.LastRosterSize} entr{(RosterSync.LastRosterSize == 1 ? "y" : "ies")} " +
                              $"({RosterSync.DeadSnapshots} dead snapshot{(RosterSync.DeadSnapshots == 1 ? "" : "s")}), " +
                              $"sent to {RosterSync.LastSendAdmins} admin(s), {RosterSync.GrantedPeerCount} granted, {Age(RosterSync.LastSendTime)} ago");
            }

            if (RavenEye.HasRenderer)
            {
                Grant.Reason reason = RavenEyeTick.ExplainGrant(znet);
                bool granted = Grant.IsGranted(reason);
                Say(args, $"  grant: {(granted ? "YES" : "no")} — {RavenEyeTick.Words(reason)}");

                if (!znet.IsServer())
                {
                    Say(args, $"  receiver registered on this connection: {(RosterSync.IsRegisteredOnCurrentConnection(znet) ? "yes" : "NO")}");
                    if (RosterSync.LastReceipt < 0f && RosterSync.LastRevoke < 0f)
                        Say(args, "  roster: never received since this join");
                    else
                        Say(args, $"  roster: {RosterSync.Current(znet).Count} entr{(RosterSync.Current(znet).Count == 1 ? "y" : "ies")}, " +
                                  $"{RosterSync.LastAppended} appended to vanilla's pins last frame; " +
                                  $"{RosterSync.PacketsReceived} received, {RosterSync.RevokesReceived} revoke(s), {RosterSync.PacketsRejected} rejected");
                    Say(args, $"  format: this build v{RosterPacket.FormatVersion}; server RavenEye {(RosterSync.ServerVersion.Length > 0 ? "v" + RosterSync.ServerVersion : "unknown — no packet yet")}" +
                              (RosterSync.PacketsRejected > 0 ? $"; last rejection: {RosterSync.LastRejectReason}" : ""));
                }

                Player local = Player.m_localPlayer;
                if (local != null)
                {
                    bool personalOff = RavenEyeTick.PersonalOptOut(local);
                    Say(args, personalOff
                        ? $"  character opt-out: THIS CHARACTER'S OWN MAP IS OFF (vanilla `nomap`, pref {RavenEyeTick.PersonalKey(local)} = 0) — `raveneye map on` clears it"
                        : "  character opt-out: none");
                }
                Minimap map = Minimap.instance;
                Say(args, $"  Game.m_noMap={Game.m_noMap}   Minimap mode={(map != null ? map.m_mode.ToString() : "no minimap")}   corrections this session: {RavenEyeTick.Reapplies} (zero is normal)");
            }
        }

        private static void Roster(Terminal.ConsoleEventArgs args)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) { Say(args, "raveneye: no world loaded."); return; }

            if (!znet.IsServer() && !RavenEyeTick.EvaluateGrant(znet))
            {
                Say(args, "raveneye: no roster — this client is not granted. See `raveneye status`.");
                return;
            }

            IList<RosterEntry> roster = RosterSync.Current(znet);
            Say(args, $"raveneye: {roster.Count} entr{(roster.Count == 1 ? "y" : "ies")}");
            for (int i = 0; i < roster.Count; i++)
            {
                RosterEntry e = roster[i];
                Vector3 p = e.Position;
                Say(args, $"  {e.Name}{(e.Dead ? " (dead — last known)" : "")}  at ({F(p.x, "0")}, {F(p.y, "0")}, {F(p.z, "0")})");
            }
        }

        private static void Map(Terminal.ConsoleEventArgs args)
        {
            string want = args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "";
            switch (want)
            {
                case "on":
                    ModConfig.ShowMap.Value = true;
                    // "Map on" plainly means the character's own vanilla opt-out too — that
                    // pref is keyed by character NAME and follows the character to every world
                    // and server, and vanilla's `nomap` command never re-applies the flag.
                    Player local = Player.m_localPlayer;
                    if (local != null && RavenEyeTick.PersonalOptOut(local))
                    {
                        PlatformPrefs.SetFloat(RavenEyeTick.PersonalKey(local), 1f);
                        Say(args, "raveneye: cleared this character's own `nomap` opt-out as well.");
                    }
                    break;
                case "off":
                    ModConfig.ShowMap.Value = false;
                    break;
                default:
                    Say(args, $"raveneye map is {(ModConfig.ShowMap.Value ? "on" : "off")}. Use `raveneye map on` or `raveneye map off`.");
                    return;
            }
            // The tick converges within half a second.
            Say(args, $"raveneye: map {(ModConfig.ShowMap.Value ? "on" : "off")} for this client" +
                      (ModConfig.ShowMap.Value ? " — it shows while the server's grant is current." : "."));
        }

        private static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        private static string Age(float since) => F(Time.realtimeSinceStartup - since, "0.#") + "s";

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args?.Context != null) args.Context.AddString(text);
            else RavenEye.Log.LogInfo(text);
        }
    }
}
