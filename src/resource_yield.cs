using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// Multiplies the resources a player actually harvests from the world: mining, lumber,
    /// hand-picked items, and (optionally) dungeon loot spawners and destructible crates.
    ///
    /// This deliberately never touches <c>CharacterDrop</c>. That method is what other loot
    /// mods replace, and patching it is what makes CreatureLevelAndLootControl's loot system
    /// stop working when DropMoreLoot is installed. Creature drops belong to Therzie's drop
    /// tables; this module only scales the world's own containers.
    ///
    /// Every vanilla path that hands out node resources funnels through
    /// <see cref="DropTable.GetDropList"/>: MineRock, MineRock5, TreeBase, TreeLog,
    /// DropOnDestroyed and LootSpawner all call it. A prefix on each owner tags its DropTable
    /// instance with a category, and the postfix scales the returned stacks. Pickables use
    /// their own amount field instead, so they get a prefix that increases the bonus count.
    /// </summary>
    internal static class ResourceYield
    {
        internal enum Kind { Mining, Lumber, Pickable, Structure }

        internal static ConfigEntry<float> MiningMultiplier;
        internal static ConfigEntry<float> LumberMultiplier;
        internal static ConfigEntry<float> PickableMultiplier;
        internal static ConfigEntry<float> StructureMultiplier;

        // A DropTable instance belongs to one component on one prefab instance, so the tag has
        // to be per instance. ConditionalWeakTable keeps dead rocks from pinning entries.
        private static readonly ConditionalWeakTable<DropTable, StrongBox<Kind>> Tags =
            new ConditionalWeakTable<DropTable, StrongBox<Kind>>();

        private static readonly HashSet<int> Seen = new HashSet<int>();

        internal static void Bind(ConfigFile config)
        {
            const string section = "Server Settings";

            MiningMultiplier = config.Bind(section, "MiningMultiplier", 1f,
                new ConfigDescription("Multiplier for ore, stone and other mining node drops. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f)));

            LumberMultiplier = config.Bind(section, "LumberMultiplier", 1f,
                new ConfigDescription("Multiplier for wood, fine wood, core wood and other tree drops. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f)));

            PickableMultiplier = config.Bind(section, "PickableMultiplier", 1f,
                new ConfigDescription("Multiplier for hand-picked items (berries, mushrooms, flint, branches). 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f)));

            StructureMultiplier = config.Bind(section, "StructureMultiplier", 1f,
                new ConfigDescription("Multiplier for destructible crates and dungeon loot spawners. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f)));
        }

        /// <summary>Tags run on every hit, so only a missing or changed category costs anything.</summary>
        private static void Tag(DropTable? table, Kind kind)
        {
            if (table == null) return;
            if (Tags.TryGetValue(table, out StrongBox<Kind> existing))
            {
                if (existing.Value != kind) existing.Value = kind;
                return;
            }
            Tags.Add(table, new StrongBox<Kind>(kind));
        }

        private static float Multiplier(Kind kind) => kind switch
        {
            Kind.Mining => MiningMultiplier.Value,
            Kind.Lumber => LumberMultiplier.Value,
            Kind.Pickable => PickableMultiplier.Value,
            Kind.Structure => StructureMultiplier.Value,
            _ => 1f,
        };

        /// <summary>Scales the stack. Unity rounds to even on .5, so 1.5x on a stack of 3 lands on 4, not 5.</summary>
        private static int Scale(int amount, float multiplier)
        {
            if (amount <= 0 || multiplier <= 1f) return amount;
            return Mathf.Max(1, Mathf.RoundToInt(amount * multiplier));
        }

        // ---- tag the owner's DropTable right before it generates its list -------------------

        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        private static class TagMineRock
        {
            private static void Prefix(MineRock __instance) => Tag(__instance.m_dropItems, Kind.Mining);
        }

        [HarmonyPatch(typeof(MineRock5), "DamageArea")]
        private static class TagMineRock5
        {
            private static void Prefix(MineRock5 __instance) => Tag(__instance.m_dropItems, Kind.Mining);
        }

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        private static class TagTreeBase
        {
            private static void Prefix(TreeBase __instance) => Tag(__instance.m_dropWhenDestroyed, Kind.Lumber);
        }

        [HarmonyPatch(typeof(TreeLog), "Destroy")]
        private static class TagTreeLog
        {
            private static void Prefix(TreeLog __instance) => Tag(__instance.m_dropWhenDestroyed, Kind.Lumber);
        }

        [HarmonyPatch(typeof(DropOnDestroyed), "OnDestroyed")]
        private static class TagDropOnDestroyed
        {
            private static void Prefix(DropOnDestroyed __instance) => Tag(__instance.m_dropWhenDestroyed, Kind.Structure);
        }

        [HarmonyPatch(typeof(LootSpawner), "UpdateSpawner")]
        private static class TagLootSpawner
        {
            private static void Prefix(LootSpawner __instance) => Tag(__instance.m_items, Kind.Structure);
        }

        // ---- scale the generated stacks ----------------------------------------------------

        // DropTable has GetDropList() and GetDropList(int). Without the empty parameter list
        // Harmony raises AmbiguousMatchException, which used to abort PatchAll and take every
        // later patch class with it.
        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
        private static class ScaleNodeDrops
        {
            private static void Postfix(DropTable __instance, List<GameObject> __result)
            {
                if (!Switches.Resources || __result == null || __result.Count == 0) return;
                if (!Tags.TryGetValue(__instance, out StrongBox<Kind> tag)) return;

                float multiplier = Multiplier(tag.Value);
                if (multiplier <= 1f) return;
                Diag.DropsScaled++;

                // CLLC's gathering multiplier duplicates the same GameObject reference in this list.
                // Scaling per entry would then compound (x2 listed twice becomes x4), so scale each
                // instance once no matter how often it appears.
                Seen.Clear();
                for (int i = 0; i < __result.Count; i++)
                {
                    GameObject drop = __result[i];
                    if (drop == null) continue;
                    if (!Seen.Add(drop.GetInstanceID())) continue;
                    ItemDrop itemDrop = drop.GetComponent<ItemDrop>();
                    if (itemDrop == null) continue;
                    ItemDrop.ItemData data = itemDrop.m_itemData;
                    if (data == null || data.m_shared == null) continue;
                    data.m_stack = Scale(data.m_stack, multiplier);
                }
            }
        }

        // ---- pickables carry their amount, not a DropTable --------------------------------

        [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
        private static class ScalePickable
        {
            private static void Prefix(Pickable __instance, ref int bonus)
            {
                if (!Switches.Resources) return;
                float multiplier = PickableMultiplier.Value;
                if (multiplier <= 1f) return;
                if (__instance == null || __instance.m_itemPrefab == null) return;

                int baseAmount = __instance.m_dontScale
                    ? __instance.m_amount
                    : Mathf.Max(__instance.m_minAmountScaled,
                        Game.instance != null
                            ? Game.instance.ScaleDrops(__instance.m_itemPrefab, __instance.m_amount)
                            : __instance.m_amount);

                // Round the boosted total, not the delta, so a fractional multiplier behaves the
                // same here as it does on node drops (1.5x on one berry gives two, not one).
                int boosted = Mathf.Max(1, Mathf.RoundToInt(baseAmount * multiplier));
                bonus += Mathf.Max(0, boosted - baseAmount);
            }
        }
    }
}
