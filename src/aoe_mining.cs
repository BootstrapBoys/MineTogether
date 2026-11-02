using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// Area-of-effect mining on every ordinary pickaxe swing, i.e. whenever vein mining is not
    /// running. On by default and switched off with [General] AreaMining; unlike AOEMining it does
    /// not need a modifier key. The swing's pickaxe damage is <em>divided</em> between
    /// the rocks in range instead of being multiplied per rock, and the radius grows with the
    /// Pickaxes skill.
    ///
    /// AOEMining ships no licence, so none of its code is used; the target list, the damage
    /// split and the skill curve are re-implemented against the game's public Damage() entry
    /// points and are configurable here.
    /// </summary>
    internal static class AoeMining
    {
        internal static ConfigEntry<bool> ScaleWithSkill;
        internal static ConfigEntry<float> RadiusMin;
        internal static ConfigEntry<float> RadiusMax;
        internal static ConfigEntry<float> BonusDamage;
        internal static ConfigEntry<int> MaxTargets;
        internal static ConfigEntry<string> AoeExcludedPrefabs;

        private static readonly Collider[] Buffer = new Collider[64];
        private static readonly HashSet<int> Seen = new HashSet<int>();
        private static readonly List<Collider> Targets = new List<Collider>(16);
        private static readonly List<Component> Rocks = new List<Component>(16);
        private static readonly Dictionary<string, bool> AllowedCache = new Dictionary<string, bool>();
        private static string[] _excluded = Array.Empty<string>();
        private static bool _redistributing;

        internal static void Bind(ConfigFile config)
        {
            const string section = "5. AOE Mining";
            ScaleWithSkill = ServerConfig.Host(config.Bind(section, "ScaleWithSkill", true,
                "Grow the AOE radius with the Pickaxes skill, from RadiusMin at skill 0 to RadiusMax at skill 100."));
            RadiusMin = ServerConfig.Host(config.Bind(section, "RadiusMin", 1f,
                new ConfigDescription("AOE radius in metres at Pickaxes 0 (or always, when ScaleWithSkill is off).",
                    new AcceptableValueRange<float>(0.5f, 8f))));
            RadiusMax = ServerConfig.Host(config.Bind(section, "RadiusMax", 4f,
                new ConfigDescription("AOE radius in metres at Pickaxes 100.",
                    new AcceptableValueRange<float>(0.5f, 10f))));
            BonusDamage = ServerConfig.Host(config.Bind(section, "BonusDamage", 0f,
                new ConfigDescription("Pickaxe damage added to the AOE swing before it is divided between targets. " +
                    "0 keeps a vanilla-strength swing.",
                    new AcceptableValueRange<float>(0f, 50f))));
            MaxTargets = ServerConfig.Host(config.Bind(section, "MaxTargets", 16,
                new ConfigDescription("Most rocks one AOE swing may spread damage over.",
                    new AcceptableValueRange<int>(1, 32))));
            AoeExcludedPrefabs = ServerConfig.Host(config.Bind(section, "AOEExcludedPrefabs", "blackmetal,obsidian,muddy,scrapiron",
                "Comma-separated name fragments. A rock whose prefab or display name contains one of these " +
                "is never hit by the AOE effect. Unrelated to ExcludedPrefabs, which filters deposits."));
            RebuildExclusions();
            AoeExcludedPrefabs.SettingChanged += (sender, args) => RebuildExclusions();
        }

        [HarmonyPatch(typeof(MineRock5), "Damage")]
        private static class MineRock5Damage
        {
            private static bool Prefix(MineRock5 __instance, HitData hit) => Spread(__instance, hit);
        }

        [HarmonyPatch(typeof(MineRock), "Damage")]
        private static class MineRockDamage
        {
            private static bool Prefix(MineRock __instance, HitData hit) => Spread(__instance, hit);
        }

        /// <summary>Returns true to let the original hit run unchanged.</summary>
        private static bool Spread(Component rock, HitData hit)
        {
            if (_redistributing || !Switches.Aoe) return true;
            // Vein mining is always active now, so this only has to stand down while a session
            // is actually running (or while the completion loop is breaking pieces). Checking
            // the enabled flag here would disable AOE permanently.
            if (Client.Running || Plugin.Completing) return true;
            // Vein mining owns deposits, including the swing that starts it. Without this the first
            // swing spread damage into the vein and broke pieces before the session began.
            if (Switches.VeinMine && Deposit.ComponentOn(rock.gameObject) != null) return true;
            if (hit == null || hit.m_damage.m_pickaxe <= 0f) return true;

            Player player = Player.m_localPlayer;
            if (player == null || hit.GetAttacker() != player) return true;
            if (!Allowed(rock)) return true;

            FillTargets(hit.m_point);
            int count = Targets.Count;
            if (count <= 1) return true;

            float total = hit.m_damage.m_pickaxe + Mathf.Max(0f, BonusDamage.Value);
            float perTarget = total / count;
            Diag.AoeSwings++;
            Diag.Trace("[AOE]", $"{count} rocks in {Radius():0.##} m, {perTarget:0.#} damage each");

            _redistributing = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Collider target = Targets[i];
                    Component affected = Rocks[i];

                    HitData distributed = hit.Clone();
                    distributed.m_damage.m_pickaxe = perTarget;
                    distributed.m_hitCollider = target;
                    distributed.m_point = target.ClosestPoint(hit.m_point);

                    if (affected is MineRock5 rock5) rock5.Damage(distributed);
                    else if (affected is MineRock rock1) rock1.Damage(distributed);
                }
            }
            finally
            {
                _redistributing = false;
            }

            return false;
        }

        private static float Radius()
        {
            if (!ScaleWithSkill.Value) return RadiusMin.Value;
            Player player = Player.m_localPlayer;
            float factor = player != null ? Mathf.Clamp01(player.GetSkillFactor(Skills.SkillType.Pickaxes)) : 0f;
            return Mathf.Lerp(RadiusMin.Value, RadiusMax.Value, factor);
        }

        private static void FillTargets(Vector3 point)
        {
            Targets.Clear();
            Rocks.Clear();
            Seen.Clear();

            float radius = Radius();
            float radiusSquared = radius * radius;
            int limit = MaxTargets.Value;
            int hits = Physics.OverlapSphereNonAlloc(point, radius, Buffer);
            for (int i = 0; i < hits && Targets.Count < limit; i++)
            {
                Collider collider = Buffer[i];
                if (collider == null) continue;
                if (!Seen.Add(collider.GetInstanceID())) continue;
                if ((collider.ClosestPoint(point) - point).sqrMagnitude > radiusSquared) continue;

                // Resolved once here and carried alongside the collider, so the apply loop does not
                // run GetComponentInParent a second time per target.
                Component rock = RockOf(collider);
                if (rock == null || !Allowed(rock)) continue;
                Targets.Add(collider);
                Rocks.Add(rock);
            }
        }

        private static Component RockOf(Collider collider)
        {
            MineRock5 rock5 = collider.GetComponentInParent<MineRock5>();
            if (rock5 != null) return rock5;
            return collider.GetComponentInParent<MineRock>();
        }

        private static bool Allowed(Component rock)
        {
            string key = rock.gameObject.name;
            if (AllowedCache.TryGetValue(key, out bool cached)) return cached;

            string identity = (rock.name + " " + key).ToLowerInvariant();
            if (rock is MineRock5 mr5) identity += " " + mr5.m_name;
            else if (rock is MineRock mr) identity += " " + mr.m_name;

            bool allowed = true;
            for (int i = 0; i < _excluded.Length; i++)
            {
                if (identity.Contains(_excluded[i]))
                {
                    allowed = false;
                    break;
                }
            }
            AllowedCache[key] = allowed;
            return allowed;
        }

        private static void RebuildExclusions()
        {
            AllowedCache.Clear();
            string excluded = AoeExcludedPrefabs.Value;
            if (string.IsNullOrEmpty(excluded))
            {
                _excluded = Array.Empty<string>();
                return;
            }
            string[] parts = excluded.Split(',');
            List<string> tokens = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim().ToLowerInvariant();
                if (token.Length > 0) tokens.Add(token);
            }
            _excluded = tokens.ToArray();
            Diag.Trace("[AOE]", $"excluded fragments: {string.Join(", ", _excluded)}");
        }
    }
}
