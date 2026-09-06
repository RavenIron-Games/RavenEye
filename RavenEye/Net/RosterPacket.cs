using System;
using System.Collections.Generic;
using UnityEngine;

namespace RavenIron.RavenEye.Net
{
    /// <summary>One online player, as the server knows them.</summary>
    public struct RosterEntry
    {
        public string Name;
        public ZDOID Id;
        public Vector3 Position;

        /// <summary>
        /// A last-known position, held through the death-to-respawn window. The pin is
        /// drawn at the snapshot with "(dead)" after the name; it does not move.
        /// </summary>
        public bool Dead;
    }

    /// <summary>What the packet says besides the entries.</summary>
    public struct RosterHeader
    {
        /// <summary>The sending mod's version string, for the status line.</summary>
        public string ServerVersion;

        /// <summary>
        /// False is an explicit revoke: the client drops the grant on receipt. The entries
        /// of a revoke are always empty.
        /// </summary>
        public bool Granted;

        /// <summary>How long the client keeps the grant if the server then goes silent.</summary>
        public float GraceSeconds;
    }

    /// <summary>
    /// The wire format, and nothing else — no engine access, so the off-game harness can
    /// round-trip it through the SHIPPING writer and reader.
    ///
    /// Layout: int formatVersion, string serverVersion, bool granted, float graceSeconds,
    /// int count, then count × (string name, ZDOID id, Vector3 pos, bool dead).
    ///
    /// THE VERSION IS IN THE PAYLOAD, NOT THE RPC NAME. The studio's other mods suffix the
    /// RPC name so a skewed pair no-ops; the critique pointed out that "no-op" is the same
    /// silence as "the server has no mod", and an admin cannot tell them apart. A version
    /// inside the packet turns a mismatch into one log line naming which side to update.
    ///
    /// The packet carries names, character ids, positions and a dead flag — and nothing else.
    /// Vanilla already hands every client every player's name, character id, platform id and
    /// display name; the single datum this mod newly discloses is the position of a player
    /// who is not sharing it, and only to admins. Frozen as a locked decision.
    /// </summary>
    public static class RosterPacket
    {
        /// <summary>Bump on any shape change. The reader refuses anything else, by name.</summary>
        public const int FormatVersion = 1;

        /// <summary>
        /// Vanilla caps a server at 10 players; modded servers go higher. This bounds a
        /// malformed count before any allocation.
        /// </summary>
        public const int MaxEntries = 64;

        public static void Write(ZPackage pkg, RosterHeader header, IList<RosterEntry> entries)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));

            int count = header.Granted ? (entries?.Count ?? 0) : 0;
            if (count > MaxEntries)
                throw new ArgumentException($"roster has {count} entries; the wire format allows {MaxEntries}");

            pkg.Write(FormatVersion);
            pkg.Write(header.ServerVersion ?? "");
            pkg.Write(header.Granted);
            pkg.Write(header.GraceSeconds);
            pkg.Write(count);
            for (int i = 0; i < count; i++)
            {
                RosterEntry e = entries[i];
                pkg.Write(e.Name ?? "");
                pkg.Write(e.Id);
                pkg.Write(e.Position);
                pkg.Write(e.Dead);
            }
        }

        /// <summary>
        /// Parse a roster. `into` is replaced ONLY on success: a truncated, malformed,
        /// non-finite or wrong-version packet throws and leaves the caller's previous roster
        /// intact, which is what the receiver wants — the next good packet is at most one
        /// interval away.
        /// </summary>
        public static RosterHeader Read(ZPackage pkg, List<RosterEntry> into)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));
            if (into == null) throw new ArgumentNullException(nameof(into));

            int version = pkg.ReadInt();
            if (version != FormatVersion)
            {
                string side = version > FormatVersion ? "this client" : "the server";
                throw new FormatException(
                    $"roster format v{version}; this build reads v{FormatVersion} — update RavenEye on {side}");
            }

            var header = new RosterHeader
            {
                ServerVersion = pkg.ReadString(),
                Granted = pkg.ReadBool(),
                GraceSeconds = pkg.ReadSingle(),
            };
            if (!IsFinite(header.GraceSeconds))
                throw new FormatException("roster grace is not a finite number");

            int count = pkg.ReadInt();
            if (count < 0 || count > MaxEntries)
                throw new FormatException($"roster count {count} is outside 0..{MaxEntries}");
            if (!header.Granted && count != 0)
                throw new FormatException("a revoke carries no entries");

            var parsed = new List<RosterEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var e = new RosterEntry
                {
                    Name = pkg.ReadString(),
                    Id = pkg.ReadZDOID(),
                    Position = pkg.ReadVector3(),
                    Dead = pkg.ReadBool(),
                };
                if (!IsFinite(e.Position.x) || !IsFinite(e.Position.y) || !IsFinite(e.Position.z))
                    throw new FormatException($"roster entry {i} has a non-finite position");
                parsed.Add(e);
            }

            into.Clear();
            into.AddRange(parsed);
            return header;
        }

        /// <summary>net472 has no float.IsFinite.</summary>
        public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
