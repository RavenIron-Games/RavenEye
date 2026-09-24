using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RavenIron.RavenEye.Config;
using RavenIron.RavenEye.Core;
using RavenIron.RavenEye.Net;
using UnityEngine;

namespace RavenEye.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program returning a nonzero exit code is enough, and adds no dependency to keep
    /// current.
    ///
    /// What it is for: the three places this mod can fail SILENTLY. A wire format that
    /// reads back plausible wrong data; a grant that is too eager (a non-admin sees a map)
    /// or too strict (an admin sees nothing and files a bug); a merge that draws a player
    /// twice or draws the admin's own marker as a pin. None of those throw in-game.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.WriteLine("RavenEye — core tests\n");

            ConfigTests();
            GraceTests();
            ExplainTests();
            DesiredNoMapTests();
            CleanNoMapTests();
            GrantTrackerTests();
            RosterPacketTests();
            RosterMergeTests();
            DeathLedgerTests();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness -------------------------------------------------------------------

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected [{expected}]\n          actual   [{actual}]");
        }

        private static Exception Throws<TEx>(Action act, string what) where TEx : Exception
        {
            try { act(); }
            catch (TEx ex) { _passed++; return ex; }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  FAIL  {what}\n          expected {typeof(TEx).Name}, got {ex.GetType().Name}: {ex.Message}");
                return ex;
            }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected {typeof(TEx).Name}, nothing was thrown");
            return null;
        }

        private static void Section(string name) => Console.WriteLine(name);

        // ---- Config --------------------------------------------------------------------

        private static void ConfigTests()
        {
            Section("ModConfig");

            var cfg = new ConfigFile();
            ModConfig.Bind(cfg);

            Check(ModConfig.ServerEnabled.Value, "server feature defaults on");
            Equal(2.0f, ModConfig.IntervalSeconds.Value, "interval defaults to vanilla's 2 s player-list cadence");
            Equal(60f, ModConfig.GraceSeconds.Value, "grace defaults to 60 s — generous, because silence is only the backstop");
            Equal(180f, ModConfig.DeathGraceSeconds.Value, "death grace defaults to 180 s");
            Check(!ModConfig.RevealWhenMapEnabled.Value, "reveal when map enabled defaults OFF — a no-map tool does nothing on a map world until told to");
            Check(!ModConfig.VerboseLogging.Value, "verbose logging defaults off");
            Check(ModConfig.ShowMap.Value, "client showMap defaults on");

            var grace = ModConfig.GraceSeconds.Description.AcceptableValues as AcceptableValueRange<float>;
            Check(grace != null && grace.Min >= Grant.MinGraceSeconds && grace.Max <= Grant.MaxGraceSeconds,
                  "the server's grace range sits inside what clients will honour, so a configured value is never silently clamped");

            var interval = ModConfig.IntervalSeconds.Description.AcceptableValues as AcceptableValueRange<float>;
            Check(interval != null && interval.Min >= 0.5f && interval.Max < Grant.MinGraceSeconds,
                  "the interval ceiling stays under the grace floor — at least one packet always fits inside any grace window");

            Equal(7, cfg.Keys.Count, "exactly seven config keys bound");
        }

        // ---- Grace / freshness -----------------------------------------------------------

        private static void GraceTests()
        {
            Section("Grant.EffectiveGrace / IsFresh");

            Equal(60f, Grant.EffectiveGrace(60f), "an in-range grace is honoured as sent");
            Equal(Grant.MinGraceSeconds, Grant.EffectiveGrace(2f), "a grace below the floor clamps UP to 10 s");
            Equal(Grant.MaxGraceSeconds, Grant.EffectiveGrace(1000f), "a grace above the ceiling clamps DOWN to 120 s");
            Equal(Grant.MinGraceSeconds, Grant.EffectiveGrace(0f), "zero → shortest window, never the longest");
            Equal(Grant.MinGraceSeconds, Grant.EffectiveGrace(-3f), "negative → shortest window");
            Equal(Grant.MinGraceSeconds, Grant.EffectiveGrace(float.NaN), "NaN → shortest window");
            Equal(Grant.MaxGraceSeconds, Grant.EffectiveGrace(float.PositiveInfinity), "+inf clamps to the ceiling");
            Equal(Grant.MinGraceSeconds, Grant.EffectiveGrace(float.NegativeInfinity), "-inf → shortest window");

            Check(!Grant.IsFresh(100f, Grant.Never, 60f), "never received → never fresh");
            Check(Grant.IsFresh(100f, 100f, 60f), "received this instant → fresh");
            Check(Grant.IsFresh(160f, 100f, 60f), "exactly at the grace edge → still fresh");
            Check(!Grant.IsFresh(160.01f, 100f, 60f), "just past the grace → stale");
            Check(!Grant.IsFresh(99f, 100f, 60f), "a receipt in the future is not evidence → stale");
            Check(Grant.IsFresh(109.9f, 100f, 1f), "a tiny grace still holds through a dropped packet (10 s floor)");
        }

        // ---- Explain -----------------------------------------------------------------------

        private static void ExplainTests()
        {
            Section("Grant.Explain");

            const float N = Grant.Never;
            // showMap, hasRenderer, isAuthority, connected, now, lastReceipt, lastRevoke, grace
            Equal(Grant.Reason.ClientOff,          Grant.Explain(false, true,  true,  true,  0f,   0f,   N, 60f), "client toggle off beats everything, even the host");
            Equal(Grant.Reason.NoRenderer,         Grant.Explain(true,  false, true,  true,  0f,   0f,   N, 60f), "no renderer beats authority (a dedicated server)");
            Equal(Grant.Reason.GrantedAsAuthority, Grant.Explain(true,  true,  true,  false, 0f,   N,    N, 60f), "authority is granted with no packet and no server peer");
            Equal(Grant.Reason.NotConnected,       Grant.Explain(true,  true,  false, false, 101f, 100f, N, 60f), "server peer gone → not connected, even with a fresh roster");
            Equal(Grant.Reason.Granted,            Grant.Explain(true,  true,  false, true,  105f, 100f, N, 60f), "connected, fresh roster → granted");
            Equal(Grant.Reason.NeverReceived,      Grant.Explain(true,  true,  false, true,  100f, N,    N, 60f), "nothing ever arrived → never received");
            Equal(Grant.Reason.Revoked,            Grant.Explain(true,  true,  false, true,  100f, N,    90f, 60f), "receipt reset by a revoke, revoke time known → revoked (not 'never received')");
            Equal(Grant.Reason.Stale,              Grant.Explain(true,  true,  false, true,  200f, 100f, N, 60f), "server silent past the grace → stale");

            Check(Grant.IsGranted(Grant.Reason.Granted) && Grant.IsGranted(Grant.Reason.GrantedAsAuthority), "both granted reasons count as granted");
            Check(!Grant.IsGranted(Grant.Reason.Stale) && !Grant.IsGranted(Grant.Reason.Revoked) && !Grant.IsGranted(Grant.Reason.NeverReceived)
                  && !Grant.IsGranted(Grant.Reason.ClientOff) && !Grant.IsGranted(Grant.Reason.NoRenderer) && !Grant.IsGranted(Grant.Reason.NotConnected),
                  "every other reason is not granted");

            // Boolean form agrees.
            Check(Grant.Evaluate(true, true, false, true, 105f, 100f, 60f), "Evaluate: fresh → true");
            Check(!Grant.Evaluate(true, true, false, true, 200f, 100f, 60f), "Evaluate: stale → false");
            Check(!Grant.Evaluate(true, true, false, false, 101f, 100f, 60f), "Evaluate: disconnected → false at once");
        }

        private static void DesiredNoMapTests()
        {
            Section("Grant.DesiredNoMap");

            // Ungranted: vanilla's own arithmetic, world || personal.
            Check(!Grant.DesiredNoMap(false, false, false), "ungranted, map world, no opt-out → map allowed");
            Check(Grant.DesiredNoMap(false, true, false), "ungranted, no-map world → locked");
            Check(Grant.DesiredNoMap(false, false, true), "ungranted, map world, personal opt-out → locked");
            Check(Grant.DesiredNoMap(false, true, true), "ungranted, both → locked");

            // Granted: the world half is relaxed, the personal half stands.
            Check(!Grant.DesiredNoMap(true, false, false), "granted, map world → allowed");
            Check(!Grant.DesiredNoMap(true, true, false), "granted, NO-MAP world → allowed — this is the whole mod");
            Check(Grant.DesiredNoMap(true, false, true), "granted, personal opt-out → still locked — the character's own choice stands");
            Check(Grant.DesiredNoMap(true, true, true), "granted, no-map world AND opt-out → locked by the opt-out");

            // The revoke that the edge-triggered design dropped: granted→false on a no-map
            // world must want the lock back, whatever the flag was left at.
            Check(Grant.DesiredNoMap(false, true, false) && !Grant.DesiredNoMap(true, true, false),
                  "revoke on a no-map world flips desired from allowed to locked — convergence has something to converge to");
        }

        private static void CleanNoMapTests()
        {
            Section("Grant.CleanNoMap");

            Check(Grant.CleanNoMap(true, false), "no-map world, not granted → vanilla's credit stands");
            Check(!Grant.CleanNoMap(true, true), "no-map world, map GRANTED → no no-map credit (the achievement is permanent)");
            Check(!Grant.CleanNoMap(false, false), "map world, not granted → never clean");
            Check(!Grant.CleanNoMap(false, true), "map world, granted → never turned INTO clean");
        }

        private static void GrantTrackerTests()
        {
            Section("GrantTracker");

            var t = new GrantTracker();
            Check(!t.Granted, "starts not granted");
            Check(!t.Update(false), "false → false is not a transition");
            Check(t.Update(true), "false → true is a transition");
            Check(t.Granted, "…and is recorded");
            Check(!t.Update(true), "true → true is not a transition");
            Check(t.Update(false), "true → false is a transition");
            t.Update(true);
            t.Reset();
            Check(!t.Granted, "reset returns to not granted");
            Check(t.Update(true), "…so the next grant is a transition again (a new world re-announces)");
        }

        // ---- RosterPacket --------------------------------------------------------------

        private static RosterEntry E(string name, long user, uint id, float x, float y, float z, bool dead = false)
            => new RosterEntry { Name = name, Id = new ZDOID(user, id), Position = new Vector3(x, y, z), Dead = dead };

        private static RosterHeader H(bool granted = true, float grace = 60f, string ver = "0.1.0")
            => new RosterHeader { ServerVersion = ver, Granted = granted, GraceSeconds = grace };

        private static List<RosterEntry> RoundTrip(RosterHeader header, IList<RosterEntry> entries, out RosterHeader read)
        {
            var w = new ZPackage();
            RosterPacket.Write(w, header, entries);
            var r = new ZPackage(w.GetArray());
            var into = new List<RosterEntry>();
            read = RosterPacket.Read(r, into);
            return into;
        }

        private static void RosterPacketTests()
        {
            Section("RosterPacket");

            Equal(1, RosterPacket.FormatVersion, "format version is 1 — bump this test WITH the constant, deliberately");
            Equal(64, RosterPacket.MaxEntries, "max entries is 64");

            // Empty grant.
            var empty = RoundTrip(H(), new List<RosterEntry>(), out RosterHeader h0);
            Equal(0, empty.Count, "empty roster round-trips empty");
            Check(h0.Granted, "granted flag survives");
            Equal(60f, h0.GraceSeconds, "grace survives an empty roster");
            Equal("0.1.0", h0.ServerVersion, "server version survives");

            // Null entries behave as empty; null version as "".
            var nul = RoundTrip(H(true, 45f, null), null, out RosterHeader hN);
            Equal(0, nul.Count, "null entry list writes as empty");
            Equal(45f, hN.GraceSeconds, "grace survives a null list");
            Equal("", hN.ServerVersion, "null server version writes as empty");

            // Several, including a non-ASCII name, a null name, and a dead snapshot.
            var src = new List<RosterEntry>
            {
                E("Skadi", 76561198000000001L, 42u, 10.5f, 31f, -120.25f),
                E("Bjørn Ævar 龍", -5L, 7u, 0f, 0f, 0f, dead: true),
                new RosterEntry { Name = null, Id = new ZDOID(9L, 9u), Position = new Vector3(1f, 2f, 3f) },
            };
            var got = RoundTrip(H(), src, out _);
            Equal(3, got.Count, "three entries round-trip");
            Equal("Skadi", got[0].Name, "ASCII name intact");
            Equal(src[0].Id, got[0].Id, "ZDOID intact");
            Equal(10.5f, got[0].Position.x, "position x intact");
            Equal(-120.25f, got[0].Position.z, "position z intact");
            Check(!got[0].Dead, "alive flag intact");
            Equal("Bjørn Ævar 龍", got[1].Name, "UTF-8 name intact");
            Equal(-5L, got[1].Id.UserID, "negative user id intact");
            Check(got[1].Dead, "dead flag intact");
            Equal("", got[2].Name, "null name is written as empty, never as a crash");
            Equal(src[1].Id, got[1].Id, "order preserved — the pin smoother matches by index");

            // A revoke carries no entries even if handed some, and reads back as not granted.
            var rev = RoundTrip(H(granted: false), src, out RosterHeader hR);
            Equal(0, rev.Count, "a revoke writes zero entries whatever it was handed");
            Check(!hR.Granted, "…and reads back as not granted");

            // A forged 'revoke with entries' is malformed.
            var forged = new ZPackage();
            forged.Write(RosterPacket.FormatVersion); forged.Write("x"); forged.Write(false); forged.Write(60f); forged.Write(1);
            forged.Write("A"); forged.Write(new ZDOID(1L, 1u)); forged.Write(new Vector3()); forged.Write(false);
            Throws<FormatException>(() => RosterPacket.Read(new ZPackage(forged.GetArray()), new List<RosterEntry>()),
                                    "a revoke that carries entries is a FormatException");

            // Read replaces `into` only on success.
            var okPkg = new ZPackage(); RosterPacket.Write(okPkg, H(), src);
            var sticky = new List<RosterEntry> { E("Old", 1L, 1u, 0f, 0f, 0f) };
            RosterPacket.Read(new ZPackage(okPkg.GetArray()), sticky);
            Equal(3, sticky.Count, "a good read replaces the previous roster");

            // Truncated packet: throws, previous roster stands.
            byte[] full = okPkg.GetArray();
            var cut = new byte[full.Length - 5];
            Array.Copy(full, cut, cut.Length);
            var kept = new List<RosterEntry> { E("Old", 1L, 1u, 0f, 0f, 0f) };
            Throws<Exception>(() => RosterPacket.Read(new ZPackage(cut), kept), "a truncated packet throws");
            Equal(1, kept.Count, "…and leaves the previous roster untouched");
            Equal("Old", kept[0].Name, "…exactly as it was");

            // Wrong format version: throws BEFORE touching the roster, and names the side to update.
            var newer = new ZPackage(); newer.Write(RosterPacket.FormatVersion + 1); newer.Write("9.9.9"); newer.Write(true); newer.Write(60f); newer.Write(0);
            var keptV = new List<RosterEntry> { E("Old", 1L, 1u, 0f, 0f, 0f) };
            var exNewer = Throws<FormatException>(() => RosterPacket.Read(new ZPackage(newer.GetArray()), keptV),
                                                  "a NEWER format version is a FormatException");
            Equal(1, keptV.Count, "…and the previous roster stands");
            Check(exNewer != null && exNewer.Message.Contains("this client"), "…naming THIS CLIENT as the side to update when the server is newer");

            var older = new ZPackage(); older.Write(0); older.Write("0.0.1"); older.Write(true); older.Write(60f); older.Write(0);
            var exOlder = Throws<FormatException>(() => RosterPacket.Read(new ZPackage(older.GetArray()), new List<RosterEntry>()),
                                                  "an OLDER format version is a FormatException");
            Check(exOlder != null && exOlder.Message.Contains("the server"), "…naming THE SERVER as the side to update when the client is newer");

            // Non-finite grace and positions are refused.
            var nanGrace = new ZPackage(); nanGrace.Write(RosterPacket.FormatVersion); nanGrace.Write("x"); nanGrace.Write(true); nanGrace.Write(float.NaN); nanGrace.Write(0);
            Throws<FormatException>(() => RosterPacket.Read(new ZPackage(nanGrace.GetArray()), new List<RosterEntry>()), "a NaN grace is a FormatException");
            var infPos = new ZPackage(); infPos.Write(RosterPacket.FormatVersion); infPos.Write("x"); infPos.Write(true); infPos.Write(60f); infPos.Write(1);
            infPos.Write("A"); infPos.Write(new ZDOID(1L, 1u)); infPos.Write(new Vector3(0f, float.PositiveInfinity, 0f)); infPos.Write(false);
            var keptP = new List<RosterEntry> { E("Old", 1L, 1u, 0f, 0f, 0f) };
            Throws<FormatException>(() => RosterPacket.Read(new ZPackage(infPos.GetArray()), keptP), "a non-finite position is a FormatException");
            Equal(1, keptP.Count, "…and the previous roster stands");

            // Malformed count — rejected before any allocation or ZDOID construction.
            var bad = new ZPackage(); bad.Write(RosterPacket.FormatVersion); bad.Write("x"); bad.Write(true); bad.Write(60f); bad.Write(-1);
            Throws<FormatException>(() => RosterPacket.Read(new ZPackage(bad.GetArray()), new List<RosterEntry>()), "negative count is a FormatException");
            var huge = new ZPackage(); huge.Write(RosterPacket.FormatVersion); huge.Write("x"); huge.Write(true); huge.Write(60f); huge.Write(int.MaxValue);
            Throws<FormatException>(() => RosterPacket.Read(new ZPackage(huge.GetArray()), new List<RosterEntry>()), "count = int.MaxValue is a FormatException, not an allocation");

            // Writer refuses an oversize roster rather than silently truncating.
            var tooMany = new List<RosterEntry>();
            for (int i = 0; i <= RosterPacket.MaxEntries; i++) tooMany.Add(E("p" + i, i + 1, 1u, 0f, 0f, 0f));
            Throws<ArgumentException>(() => RosterPacket.Write(new ZPackage(), H(), tooMany), "writing more than MaxEntries throws");

            // Exactly MaxEntries is fine.
            tooMany.RemoveAt(tooMany.Count - 1);
            var max = RoundTrip(H(), tooMany, out _);
            Equal(RosterPacket.MaxEntries, max.Count, "exactly MaxEntries round-trips");

            // Byte-level sanity: version(4) + "0.1.0"(1+5) + granted(1) + grace(4) + count(4) = 19 bytes.
            var e = new ZPackage(); RosterPacket.Write(e, H(), null);
            Equal(19L, e.Size, "empty roster is 19 bytes on the wire");

            // Nulls.
            Throws<ArgumentNullException>(() => RosterPacket.Write(null, H(), src), "null package on write throws");
            Throws<ArgumentNullException>(() => RosterPacket.Read(null, new List<RosterEntry>()), "null package on read throws");
            Throws<ArgumentNullException>(() => RosterPacket.Read(new ZPackage(), null), "null target on read throws");
        }

        // ---- RosterMerge ---------------------------------------------------------------

        private static ZNet.PlayerInfo P(string name, long user, uint id, float x)
            => new ZNet.PlayerInfo { m_name = name, m_characterID = new ZDOID(user, id), m_publicPosition = true, m_position = new Vector3(x, 0f, 0f) };

        private static void RosterMergeTests()
        {
            Section("RosterMerge");

            var self = new ZDOID(100L, 1u);

            // Plain append.
            var list = new List<ZNet.PlayerInfo>();
            var roster = new List<RosterEntry> { E("A", 1L, 1u, 1f, 0f, 0f), E("B", 2L, 1u, 2f, 0f, 0f) };
            Equal(2, RosterMerge.Append(list, roster, self), "two roster entries appended to an empty list");
            Equal("A", list[0].m_name, "name carried");
            Equal(2f, list[1].m_position.x, "position carried");
            Check(list[0].m_publicPosition && list[1].m_publicPosition, "appended entries are marked public — vanilla's pin code requires it");

            // Self excluded.
            list.Clear();
            roster = new List<RosterEntry> { E("Me", 100L, 1u, 5f, 0f, 0f), E("A", 1L, 1u, 1f, 0f, 0f) };
            Equal(1, RosterMerge.Append(list, roster, self), "the local character is not appended");
            Equal("A", list[0].m_name, "…only the other player is");

            // Duplicates by character id excluded (a sharing player is already in vanilla's list).
            list = new List<ZNet.PlayerInfo> { P("A-vanilla", 1L, 1u, 1f) };
            roster = new List<RosterEntry> { E("A", 1L, 1u, 1.5f, 0f, 0f), E("B", 2L, 1u, 2f, 0f, 0f) };
            Equal(1, RosterMerge.Append(list, roster, self), "an id already in the list is skipped");
            Equal(2, list.Count, "one vanilla + one appended");
            Equal("A-vanilla", list[0].m_name, "vanilla's entry is untouched — vanilla wins");
            Equal("B", list[1].m_name, "the new one follows");

            // Duplicates WITHIN the roster excluded too.
            list.Clear();
            roster = new List<RosterEntry> { E("B", 2L, 1u, 2f, 0f, 0f), E("B-again", 2L, 1u, 3f, 0f, 0f) };
            Equal(1, RosterMerge.Append(list, roster, self), "a repeated id within the roster appends once");

            // None ids skipped.
            list.Clear();
            roster = new List<RosterEntry> { new RosterEntry { Name = "Loading", Id = ZDOID.None, Position = new Vector3() } };
            Equal(0, RosterMerge.Append(list, roster, self), "a None character id is skipped");

            // Dead entries keep their id and get the suffix.
            list.Clear();
            roster = new List<RosterEntry> { E("Fallen", 3L, 3u, 7f, 8f, 9f, dead: true) };
            Equal(1, RosterMerge.Append(list, roster, self), "a dead snapshot is appended");
            Equal("Fallen" + RosterMerge.DeadSuffix, list[0].m_name, "…with the dead suffix on the name — the only field a vanilla pin shows");
            Equal(7f, list[0].m_position.x, "…at the snapshot position");

            // Same user id, different object id, is a different character.
            list.Clear();
            roster = new List<RosterEntry> { E("Alt", 100L, 2u, 1f, 0f, 0f) };
            Equal(1, RosterMerge.Append(list, roster, self), "same user, different character id → not self");

            // Order preserved, in the server's order.
            list.Clear();
            roster = new List<RosterEntry> { E("Z", 26L, 1u, 0f, 0f, 0f), E("A", 1L, 1u, 0f, 0f, 0f), E("M", 13L, 1u, 0f, 0f, 0f) };
            RosterMerge.Append(list, roster, self);
            Equal("Z", list[0].m_name, "order: first stays first");
            Equal("M", list[2].m_name, "order: last stays last — no sorting, the pin smoother matches by index");

            // Nulls are a no-op.
            Equal(0, RosterMerge.Append(null, roster, self), "null target → 0");
            Equal(0, RosterMerge.Append(list, null, self), "null roster → 0");

            // Null name becomes empty.
            list.Clear();
            roster = new List<RosterEntry> { new RosterEntry { Name = null, Id = new ZDOID(7L, 7u), Position = new Vector3() } };
            RosterMerge.Append(list, roster, self);
            Equal("", list[0].m_name, "null name appends as empty, never as null (vanilla compares names)");

            // Nothing in a roster entry can make the merge throw: a poisoned entry cannot escape
            // into Minimap.Update's frame.
            list.Clear();
            roster = new List<RosterEntry>
            {
                default(RosterEntry),
                new RosterEntry { Name = new string('x', 10000), Id = new ZDOID(long.MinValue, uint.MaxValue), Position = new Vector3(float.NaN, float.PositiveInfinity, float.NegativeInfinity), Dead = true },
            };
            int n = -1;
            try { n = RosterMerge.Append(list, roster, self); } catch { }
            Equal(1, n, "default and extreme entries merge without throwing (the default one is None and skipped)");
        }

        // ---- DeathLedger ---------------------------------------------------------------
        // The review found the first draft's dead pin lived only between the end of the death
        // screen and the respawn. These drive the ledger through the whole story.

        private static int Emit(DeathLedger l, float now, float grace, out List<RosterEntry> into)
        {
            into = new List<RosterEntry>();
            return l.Emit(into, now, grace);
        }

        private static void DeathLedgerTests()
        {
            Section("DeathLedger");

            const long uid = 42L;
            var alive1 = E("Bjorn", 7L, 100u, 10f, 0f, 20f);   // first life
            var alive2 = E("Bjorn", 7L, 101u, 0f, 0f, 0f);     // respawned: NEW character id, spawn point

            // Alive, nothing dead.
            var l = new DeathLedger();
            l.Observe(uid, true, alive1, 0f);
            Equal(0, Emit(l, 0f, 180f, out _), "alive player emits no death");
            Equal(1, l.LiveCount, "one live entry");

            // Death observed as the id going None (vanilla's _RequestRespawn, ~10 s after death).
            l.Observe(uid, false, default(RosterEntry), 12f);
            int n = Emit(l, 12f, 180f, out List<RosterEntry> dead);
            Equal(1, n, "death emitted while the id is None");
            Check(dead[0].Dead, "…marked dead");
            Equal(alive1.Id, dead[0].Id, "…under the OLD character id");
            Equal(10f, dead[0].Position.x, "…at the last LIVE position, not wherever the reference position went");
            Equal("Bjorn", dead[0].Name, "…with the plain name (the merge adds the suffix)");

            // THE REVIEW'S FINDING: the corpse must survive the respawn.
            l.Observe(uid, true, alive2, 20f);
            n = Emit(l, 20f, 180f, out dead);
            Equal(1, n, "after respawn the death is STILL emitted");
            Equal(alive1.Id, dead[0].Id, "…still under the old id, distinct from the live pin's new id");
            Equal(1, l.LiveCount, "…and the respawned player is live again");

            // It lasts the grace, then goes.
            Equal(1, Emit(l, 12f + 180f, 180f, out _), "at exactly the grace edge the death is still served");
            Equal(0, Emit(l, 12f + 180.01f, 180f, out _), "past the grace it is dropped");
            Equal(0, l.DeadCount, "…and forgotten");

            // A respawn that a cadence straddled: id changes with no None observed.
            l = new DeathLedger();
            l.Observe(uid, true, alive1, 0f);
            l.Observe(uid, true, alive2, 30f);
            n = Emit(l, 30f, 180f, out dead);
            Equal(1, n, "an id CHANGE between cadences is a death too");
            Equal(alive1.Id, dead[0].Id, "…snapshotting the previous life");
            Equal(10f, dead[0].Position.x, "…at its last position");

            // Dying again within the grace: latest death wins, one pin.
            l.Observe(uid, false, default(RosterEntry), 45f);
            n = Emit(l, 45f, 180f, out dead);
            Equal(1, n, "a second death replaces the first — one corpse pin per player");
            Equal(alive2.Id, dead[0].Id, "…the newer one");

            // Disconnect prunes both stores.
            l = new DeathLedger();
            l.Observe(uid, true, alive1, 0f);
            l.Observe(uid, false, default(RosterEntry), 5f);
            l.Observe(99L, true, E("Other", 8L, 1u, 0f, 0f, 0f), 5f);
            l.Retain(new List<long> { 99L });
            Equal(0, Emit(l, 6f, 180f, out _), "a player who disconnected takes their corpse pin with them");
            Equal(1, l.LiveCount, "…and only the present player stays live");

            // Grace zero disables and clears.
            l = new DeathLedger();
            l.Observe(uid, true, alive1, 0f);
            l.Observe(uid, false, default(RosterEntry), 5f);
            Equal(0, Emit(l, 5f, 0f, out _), "grace 0 emits nothing");
            Equal(0, l.DeadCount, "…and clears the store");

            // Never-spawned player: None from the start is not a death.
            l = new DeathLedger();
            l.Observe(uid, false, default(RosterEntry), 0f);
            Equal(0, Emit(l, 0f, 180f, out _), "a player who has not spawned yet is not dead");

            // Clear.
            l.Observe(uid, true, alive1, 1f);
            l.Clear();
            Equal(0, l.LiveCount + l.DeadCount, "Clear empties both stores");
        }
    }
}
