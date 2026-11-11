// =====================================================================
//  SEED APOCALYPSE  -  PvZ Fusion 4.0.5.2 challenge mode (MelonLoader 0.7.x, Il2Cpp)
//
//  What it does
//   * Adds a "Seed Apocalypse" launcher on the main menu (button, or F9).
//     F10 cycles difficulty (Hard / Brutal / Nightmare).
//   * Runs on top of the game's own SeedRain challenge, but replaces the
//     seed pool with EVERY plant in the PlantType enum (basic, fusions,
//     super, ultimate, doom/gold/silver variants, "advanced" fusions, ...),
//     weighted so basics are common and ultimates are rare jackpots.
//   * Zombies scale every wave, plus extra zombies are injected each wave
//     with a rotating wave modifier (Iron Tide / Swarm / Titans / Pincer),
//     a mini-boss every 5th wave and an APOCALYPSE wave every 10th.
//
//  Everything is wrapped in try/catch so a wrong guess about the game's
//  internals logs a warning instead of crashing the game.
// =====================================================================
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using BoardConfig = Il2CppGameLevel.BoardConfig; // lives in a different namespace than Board

[assembly: MelonInfo(typeof(SeedApocalypse.SeedApocalypseMod), "Seed Apocalypse", "1.0.0", "Claude")]
[assembly: MelonGame(null, null)]

namespace SeedApocalypse
{
    public class SeedApocalypseMod : MelonMod
    {
        // ---- state -----------------------------------------------------
        internal static bool Active;          // our challenge is the one being played
        internal static bool OnMenu;          // main menu is showing
        internal static int LastWave = -1;    // last wave we injected extras for
        internal static bool Injecting;       // re-entrancy guard
        internal static float PendingHealthMult = 1f; // applied by the SetZombie patch
        internal static string WaveTag = "";  // HUD text for the current wave modifier
        internal static MelonLogger.Instance Log;

        // ---- preferences (UserData/MelonPreferences.cfg) ----------------
        internal static MelonPreferences_Entry<int> PrefDifficulty;   // 1 Hard, 2 Brutal, 3 Nightmare
        internal static MelonPreferences_Entry<int> PrefMaxWave;      // 0 = leave the level's own value
        internal static MelonPreferences_Entry<int> PrefMaxExtras;    // cap on injected zombies per wave

        internal static readonly string[] DiffNames = { "", "Hard", "Brutal", "Nightmare" };
        internal static readonly float[] DiffCount = { 0f, 0.7f, 1.0f, 1.45f };
        internal static readonly float[] DiffHealth = { 0f, 1.5f, 2.5f, 4.0f };

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var cat = MelonPreferences.CreateCategory("SeedApocalypse");
            PrefDifficulty = cat.CreateEntry("Difficulty", 2, description: "1 = Hard, 2 = Brutal, 3 = Nightmare");
            PrefMaxWave = cat.CreateEntry("WinAtWave", 40, description: "Wave to survive for the win (0 = keep level default)");
            PrefMaxExtras = cat.CreateEntry("MaxExtraZombiesPerWave", 40, description: "Safety cap on injected zombies");
            Log.Msg("Seed Apocalypse loaded. Main menu: F9 = start, F10 = difficulty.");
        }

        // ---- input / launcher ------------------------------------------
        public override void OnUpdate()
        {
            if (!OnMenu) return;
            if (Input.GetKeyDown(KeyCode.F10)) CycleDifficulty();
            if (Input.GetKeyDown(KeyCode.F9)) Launch();
        }

        internal static void CycleDifficulty()
        {
            int d = PrefDifficulty.Value + 1;
            if (d > 3) d = 1;
            PrefDifficulty.Value = d;
            MelonPreferences.Save();
        }

        internal static void Launch()
        {
            try
            {
                Active = true;
                LastWave = -1;
                // Levels are looked up by (LevelType, number). SeedRain is an existing challenge
                // whose card drops come from Board.seedPool, which we replace.
                UIMgr.EnterGame(LevelType.Challenge, (int)ChallengeLevel.SeedRain, 0, "");
            }
            catch (Exception e)
            {
                Active = false;
                Log.Error("Could not start Seed Apocalypse: " + e);
            }
        }

        // ---- IMGUI: menu button + in-game HUD ---------------------------
        public override void OnGUI()
        {
            try
            {
                if (OnMenu)
                {
                    float w = 260f, x = Screen.width - w - 12f;
                    if (GUI.Button(new Rect(x, 12f, w, 40f), "SEED APOCALYPSE  (F9)")) Launch();
                    if (GUI.Button(new Rect(x, 58f, w, 28f), "Difficulty: " + DiffNames[Mathf.Clamp(PrefDifficulty.Value, 1, 3)] + "  (F10)")) CycleDifficulty();
                    return;
                }

                if (!Active) return;
                Board b = Board.Instance;
                if (b == null) return;
                string text = "SEED APOCALYPSE  |  Wave " + b.theWave + " / " + b.theMaxWave
                              + "  |  " + DiffNames[Mathf.Clamp(PrefDifficulty.Value, 1, 3)]
                              + (string.IsNullOrEmpty(WaveTag) ? "" : "  |  " + WaveTag);
                GUI.Box(new Rect(10f, 6f, 560f, 26f), text);
            }
            catch { /* never let HUD errors spam the log */ }
        }

        // ---- seed list ---------------------------------------------------
        static List<PlantType> _weightedSeeds;

        static bool Excluded(string n)
        {
            if (n == "value__" || n == "Nothing" || n == "MixAnim") return true;
            if (n.StartsWith("Tower_")) return true;                 // tower-mode-only plants
            if (n.EndsWith("_a") || n.EndsWith("_b") || n.EndsWith("_c")) return true; // animation stage helpers
            switch (n)
            {
                case "ImitateWheat": case "Wheat": case "ObsidianWheat":
                case "Extract_single": case "Extract_ten":
                case "PresentZombie": case "ZombieEndoFlame": case "GoldScaryPot":
                    return true;
            }
            return false;
        }

        static int Weight(PlantType t)
        {
            // Basics are common, fusions less so, ultimates are rare jackpots.
            try
            {
                if (Lawnf.IsUltiPlant(t)) return 1;
                if (Lawnf.IsSuperPlant(t)) return 2;
                if (Lawnf.IsSecondPlant(t)) return 3;
                if (Lawnf.IsBasicPlant(t)) return 4;
            }
            catch { }
            return 3;
        }

        internal static List<PlantType> GetWeightedSeeds()
        {
            if (_weightedSeeds != null) return _weightedSeeds;
            var list = new List<PlantType>();
            int distinct = 0;
            foreach (PlantType t in Enum.GetValues(typeof(PlantType)))
            {
                if (Excluded(t.ToString())) continue;
                distinct++;
                int w = Weight(t);
                for (int i = 0; i < w; i++) list.Add(t);
            }
            Log.Msg("Seed pool built: " + distinct + " distinct plants (" + list.Count + " weighted entries).");
            _weightedSeeds = list;
            return list;
        }

        // ---- zombie tiers ------------------------------------------------
        static readonly string[][] TierNames =
        {
            // 0: waves 1-9
            new[] { "ConeZombie","BucketZombie","FootballZombie","PolevaulterZombie","JackboxZombie","DancePolZombie",
                    "CatapultZombie","LadderZombie","PogoZombie","IronConeZombie","PaperZombie","MinerZombie" },
            // 1: waves 10-19
            new[] { "Gargantuar","RedGargantuar","SuperJackboxZombie","MachineNutZombie","SuperPogoZombie","BlackFootball",
                    "GatlingFootballZombie","SuperKirov","SuperSubmarine","JacksonDriver","DoomZombie","SuperPolevaulter","IronGargantuar" },
            // 2: waves 20-29
            new[] { "SuperGargantuar","IronRedGargantuar","UltimateFootballZombie","UltimateJackboxZombie","UltimatePaperZombie",
                    "UltimateMachineNutZombie","UltimateKirovZombie","UltimateJacksonDriver","SuperBlackFootball","BlackElephantZombie",
                    "IceClawZombie","UltimateSnowZombie" },
            // 3: wave 30+
            new[] { "UltimateGargantuar","UltimateGoldGargantuar","UltimateLegionZombie","UltimateSwordZombie","UltimateEndoflameZombie",
                    "ArmedGargantuar","UltimateImpKing","UltimateDolphin","SuperBlackHorse","BlackTrainZombie","UltiWaterGargantuar" },
        };
        static readonly string[] TitanNames =
            { "Gargantuar","RedGargantuar","IronGargantuar","SuperGargantuar","UltimateGargantuar","UltimateGoldGargantuar","ArmedGargantuar" };

        static List<ZombieType>[] _tiers;
        static List<ZombieType> _titans;

        static List<ZombieType> Resolve(string[] names)
        {
            var r = new List<ZombieType>();
            foreach (var n in names)
            {
                if (Enum.TryParse<ZombieType>(n, out var z)) r.Add(z);
                else Log.Warning("Unknown ZombieType skipped: " + n);
            }
            return r;
        }

        static void EnsureTiers()
        {
            if (_tiers != null) return;
            _tiers = new List<ZombieType>[TierNames.Length];
            for (int i = 0; i < TierNames.Length; i++) _tiers[i] = Resolve(TierNames[i]);
            _titans = Resolve(TitanNames);
        }

        static ZombieType Pick(List<ZombieType> l) => l[UnityEngine.Random.Range(0, l.Count)];

        // ---- wave director ----------------------------------------------
        internal static void InjectWave(BoardSpawner spawner, Board b, int wave)
        {
            EnsureTiers();
            int diff = Mathf.Clamp(PrefDifficulty.Value, 1, 3);

            int tier = Mathf.Min(3, wave / 10);
            float count = (1f + wave * 0.30f) * DiffCount[diff];
            float hp = 1f;
            string tag = "";
            bool titans = false, pincer = false;
            int pincerRow = -1;

            if (wave > 0 && wave % 10 == 0)
            {
                tag = "APOCALYPSE WAVE"; count *= 2f; hp = 1.5f; tier = Mathf.Min(3, tier + 1);
            }
            else if (wave > 0 && wave % 5 == 0)
            {
                tag = "MINI-BOSS"; // handled below with a guaranteed top-tier zombie
            }
            else
            {
                switch (wave % 5)
                {
                    case 1: tag = "Iron Tide (+50% HP)"; hp = 1.5f; break;
                    case 2: tag = "Swarm (x1.6 count)"; count *= 1.6f; hp = 0.8f; break;
                    case 3: tag = "Titans"; titans = true; count *= 0.4f; hp = 1.3f; break;
                    case 4:
                        tag = "Pincer (one row)"; pincer = true;
                        pincerRow = spawner.GetRandomRow(); break;
                }
            }
            WaveTag = tag;

            int n = Mathf.Clamp(Mathf.CeilToInt(count), 1, PrefMaxExtras.Value);
            Log.Msg("Wave " + wave + ": " + (tag == "" ? "standard" : tag) + " -> +" + n + " zombies (tier " + tier + ")");

            PendingHealthMult = hp;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    ZombieType type;
                    if (titans) type = Pick(_titans);
                    else
                    {
                        float roll = UnityEngine.Random.value;
                        int t = tier;
                        if (roll > 0.90f) t = Mathf.Min(3, tier + 1);   // occasional above-curve spike
                        else if (roll < 0.20f) t = Mathf.Max(0, tier - 1);
                        var list = _tiers[t];
                        if (list.Count == 0) continue;
                        type = Pick(list);
                    }
                    Spawn(spawner, type, pincer ? pincerRow : -1);
                }

                if (tag == "MINI-BOSS")
                {
                    var top = _tiers[Mathf.Min(3, tier + 1)];
                    if (top.Count > 0) Spawn(spawner, Pick(top), -1);
                }
            }
            finally { PendingHealthMult = 1f; }
        }

        static void Spawn(BoardSpawner spawner, ZombieType type, int forcedRow)
        {
            try
            {
                int row = forcedRow >= 0 ? forcedRow : spawner.GetRandomRow();
                ZombieType fixedType = spawner.AdjustZombieForTerrain(type, ref row); // water/land/roof fixes
                CreateZombie.Instance.SetZombie(row, fixedType, 0f, false);
            }
            catch (Exception e) { Log.Warning("Spawn failed for " + type + ": " + e.Message); }
        }
    }

    // =====================================================================
    //  Harmony patches
    // =====================================================================

    // Main menu is up: show launcher, clear any stale "active" flag.
    [HarmonyPatch(typeof(MainMenu), "Start")]
    static class Patch_MainMenu_Start
    {
        static void Postfix()
        {
            SeedApocalypseMod.OnMenu = true;
            SeedApocalypseMod.Active = false;
            SeedApocalypseMod.WaveTag = "";
        }
    }

    // Left the menu.
    [HarmonyPatch(typeof(Board), "Awake")]
    static class Patch_Board_Awake
    {
        static void Postfix() { SeedApocalypseMod.OnMenu = false; }
    }

    // Board is starting: toughen the config (on a COPY so the real level is untouched).
    [HarmonyPatch(typeof(Board), "Start")]
    static class Patch_Board_Start
    {
        static void Postfix(Board __instance)
        {
            if (!SeedApocalypseMod.Active) return;
            try
            {
                SeedApocalypseMod.LastWave = -1;
                int diff = Mathf.Clamp(SeedApocalypseMod.PrefDifficulty.Value, 1, 3);

                BoardConfig cfg = __instance.config != null ? __instance.config.Copy() : new BoardConfig();
                cfg.zombieHealthMultiplier = Mathf.Max(cfg.zombieHealthMultiplier, 1f);
                cfg.zombieCountMultiplier = Mathf.Max(cfg.zombieCountMultiplier, 1f) * (diff == 3 ? 1.5f : 1.2f);
                if (cfg.waveInterval > 0f) cfg.waveInterval *= (diff == 3 ? 0.7f : 0.85f);
                __instance.config = cfg;

                int win = SeedApocalypseMod.PrefMaxWave.Value;
                if (win > 0) __instance.theMaxWave = win;
            }
            catch (Exception e) { SeedApocalypseMod.Log.Warning("Board config tweak failed: " + e.Message); }
        }
    }

    // THE SEEDS: every plant in the game, weighted.
    [HarmonyPatch(typeof(Board), "InitSeedPool")]
    static class Patch_Board_InitSeedPool
    {
        static void Postfix(Board __instance)
        {
            if (!SeedApocalypseMod.Active) return;
            try
            {
                var pool = new Il2CppSystem.Collections.Generic.List<PlantType>();
                foreach (var t in SeedApocalypseMod.GetWeightedSeeds()) pool.Add(t);
                __instance.seedPool = pool;
            }
            catch (Exception e) { SeedApocalypseMod.Log.Error("Seed pool override failed: " + e); }
        }
    }

    // THE ESCALATION: extra zombies and wave modifiers, once per wave.
    [HarmonyPatch(typeof(BoardSpawner), "SummonZombies")]
    static class Patch_BoardSpawner_SummonZombies
    {
        static void Postfix(BoardSpawner __instance)
        {
            if (!SeedApocalypseMod.Active || SeedApocalypseMod.Injecting) return;
            Board b = Board.Instance;
            if (b == null) return;
            int wave = b.theWave;
            if (wave == SeedApocalypseMod.LastWave) return;   // SummonZombies may fire several times per wave
            SeedApocalypseMod.LastWave = wave;

            SeedApocalypseMod.Injecting = true;
            try { SeedApocalypseMod.InjectWave(__instance, b, wave); }
            catch (Exception e) { SeedApocalypseMod.Log.Warning("Wave injection failed: " + e.Message); }
            finally { SeedApocalypseMod.Injecting = false; }
        }
    }

    // HP scaling for every zombie (vanilla + injected).
    [HarmonyPatch(typeof(CreateZombie), "SetZombie")]
    static class Patch_CreateZombie_SetZombie
    {
        static void Postfix(Zombie __result)
        {
            if (!SeedApocalypseMod.Active || __result == null) return;
            try
            {
                Board b = Board.Instance;
                int wave = b != null ? b.theWave : 0;
                int diff = Mathf.Clamp(SeedApocalypseMod.PrefDifficulty.Value, 1, 3);
                float m = SeedApocalypseMod.DiffHealth[diff]
                          * (1f + Mathf.Min(wave, 60) * 0.04f)
                          * SeedApocalypseMod.PendingHealthMult;
                long max = (long)(__result.theMaxHealth * m);
                __result.theMaxHealth = max;
                __result.theHealth = max;
            }
            catch { }
        }
    }
}
