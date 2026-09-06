using BepInEx.Configuration;

namespace RavenIron.RavenEye.Config
{
    /// <summary>
    /// The whole config surface. One file serves every role; the section name says which
    /// side of the wire each value is read on. A value in the wrong section is not an error,
    /// it is simply never read — so the descriptions say so explicitly.
    /// </summary>
    public static class ModConfig
    {
        // ---- Server (read where the world runs: dedicated server or listen host) --------

        /// <summary>Master switch for the feature on this world. Gates the roster BUILDER, so a listen host is bound by it too.</summary>
        public static ConfigEntry<bool> ServerEnabled;

        /// <summary>
        /// How often the roster goes out. Vanilla's own player list is every 2 s, and the
        /// positions it carries are refreshed by each client on that same cadence, so going
        /// faster buys nothing.
        /// </summary>
        public static ConfigEntry<float> IntervalSeconds;

        /// <summary>
        /// How long an admin client keeps its map after the LAST roster if the server goes
        /// silent. This is a backstop for a stalled or vanished server, not the revoke
        /// mechanism: a demotion is sent explicitly and relocks within one interval. Generous
        /// by design — a false revoke during an autosave stall would slam the map shut under
        /// the admin's cursor, and a demoted admin keeping a map for another minute costs
        /// nothing.
        /// </summary>
        public static ConfigEntry<float> GraceSeconds;

        /// <summary>
        /// After a player dies, keep serving their last known position for this long, marked
        /// as dead. Zero disables. On a no-map world the commonest admin request is "help me
        /// find where I died", and vanilla drops the character id the moment respawn begins.
        /// </summary>
        public static ConfigEntry<float> DeathGraceSeconds;

        /// <summary>
        /// On a world that HAS a map, still show admins the players who switched position
        /// sharing off. Default off: the critique panel was unanimous that a tool named for
        /// no-map worlds should do nothing on any other kind until an owner says so. On a
        /// no-map world this is moot: nobody can share anything, so the roster always goes out.
        /// Gates the BUILDER, not just the send, so a listen host cannot bypass it.
        /// </summary>
        public static ConfigEntry<bool> RevealWhenMapEnabled;

        /// <summary>Per-send chatter in the log. Grants and revocations log regardless.</summary>
        public static ConfigEntry<bool> VerboseLogging;

        // ---- Client (read where a screen is: an admin's game, or a listen host) -----------

        /// <summary>
        /// Let this client use the map the server grants it. Off means: keep the vanilla
        /// no-map experience even though the server would allow otherwise — for an admin who
        /// wants to play their own world honestly. `raveneye map on|off` flips it at runtime.
        /// </summary>
        public static ConfigEntry<bool> ShowMap;

        public static void Bind(ConfigFile cfg)
        {
            ServerEnabled = cfg.Bind("Server", "Enabled", true,
                new ConfigDescription(
                    "Send admins the position of every online player. Read on the SERVER (dedicated or " +
                    "listen host); a client's copy of this value does nothing."));

            IntervalSeconds = cfg.Bind("Server", "IntervalSeconds", 2.0f,
                new ConfigDescription(
                    "Seconds between roster sends. Vanilla refreshes player positions every 2 s, so a " +
                    "smaller value only costs bandwidth; the ceiling stays under the shortest grace window so a live server can never be mistaken for a silent one. Read on the SERVER.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            GraceSeconds = cfg.Bind("Server", "GraceSeconds", 60f,
                new ConfigDescription(
                    "If the server goes SILENT (crash, stall, network), an admin's map survives this many " +
                    "seconds after the last roster before it relocks. A demotion does not wait for this: it " +
                    "is sent explicitly and relocks within one interval. Clients clamp what they receive to " +
                    "10..120. Read on the SERVER.",
                    new AcceptableValueRange<float>(10f, 120f)));

            DeathGraceSeconds = cfg.Bind("Server", "DeathGraceSeconds", 180f,
                new ConfigDescription(
                    "After a player dies, keep their last known position on the admins' map, marked (dead), " +
                    "for this many seconds — so an admin can answer 'where did I die?' on a world with no " +
                    "map. 0 disables. Read on the SERVER.",
                    new AcceptableValueRange<float>(0f, 600f)));

            RevealWhenMapEnabled = cfg.Bind("Server", "RevealWhenMapEnabled", false,
                new ConfigDescription(
                    "On a world WITH a map, also show admins the players who turned position sharing off. " +
                    "Off by default: out of the box RavenEye is a no-map tool and does nothing on a world " +
                    "that has a map, where players' share-position choice stands. Read on the SERVER."));

            VerboseLogging = cfg.Bind("Server", "VerboseLogging", false,
                new ConfigDescription(
                    "Log every roster send. Grants and revocations are logged regardless. Read on the SERVER."));

            ShowMap = cfg.Bind("Client", "ShowMap", true,
                new ConfigDescription(
                    "Use the map when the server grants it to this client. Set false to keep the vanilla " +
                    "no-map experience anyway (an admin playing honestly). `raveneye map on|off` toggles " +
                    "this at runtime. Read on the CLIENT."));
        }
    }
}
