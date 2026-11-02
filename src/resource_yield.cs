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

        // DropTable.GetDropList() hands out prefab references, so the postfix only *records* the
        // multiplier for this frame; the stack is scaled on the instantiated clone, see ScaleOnSpawn.
        private static int _pendingFrame = -1;
        private static float _pendingMultiplier = 1f;

        internal static void Bind(ConfigFile config)
        {
            const string section = "3. Resources";

            MiningMultiplier = ServerConfig.Host(config.Bind(section, "MiningMultiplier", 1f,
                new ConfigDescription("Multiplier for ore, stone and other mining node drops. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f))));

            LumberMultiplier = ServerConfig.Host(config.Bind(section, "LumberMultiplier", 1f,
                new ConfigDescription("Multiplier for wood, fine wood, core wood and other tree drops. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f))));

            PickableMultiplier = ServerConfig.Host(config.Bind(section, "PickableMultiplier", 1f,
                new ConfigDescription("Multiplier for hand-picked items (berries, mushrooms, flint, branches). 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f))));

            StructureMultiplier = ServerConfig.Host(config.Bind(section, "StructureMultiplier", 1f,
                new ConfigDescription("Multiplier for destructible crates and dungeon loot spawners. 1 = vanilla.",
                    new AcceptableValueRange<float>(1f, 10f))));
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

        /// <summary>
        /// The whole part of the multiplier is guaranteed; the fraction is a weighted coin flip, so
        /// 1.5x on one item gives two items half the time instead of two every time. Averages are
        /// exactly the configured multiplier without ever rounding a player's half up on every drop.
        /// </summary>
        private static int Extra(int amount, float multiplier)
        {
            if (amount <= 0 || multiplier <= 1f) return 0;
            float exact = amount * (multiplier - 1f);
            int whole = Mathf.FloorToInt(exact);
            float fraction = exact - whole;
            if (fraction > 0f && UnityEngine.Random.value < fraction) whole++;
            return whole;
        }

        /// <summary>Total items this drop should carry, before the stack-size split.</summary>
        private static int Scale(int amount, float multiplier)
        {
            if (amount <= 0) return amount;
            return Mathf.Max(1, amount + Extra(amount, multiplier));
        }

        /// <summary>A stack is legal only up to the item's own limit.</summary>
        private static int MaxStack(ItemDrop drop)
        {
            ItemDrop.ItemData.SharedData shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            return shared != null ? shared.m_maxStackSize : 0;
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
                _pendingFrame = Time.frameCount;
                _pendingMultiplier = multiplier;
            }
        }

        // ---- scale the clone, once, on the frame the list was generated ---------------------

        /// <summary>
        /// Every drop path instantiates the listed prefabs and hands the clone to
        /// <see cref="ItemDrop.OnCreateNew"/>. Scaling here touches that instance only: the prefab's
        /// shared ItemData is never written, so a node whose areas break one at a time no longer
        /// multiplies the table again on each area (which compounded 2x into 2^30 per node).
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new Type[] { typeof(ItemDrop), typeof(bool) })]
        private static class ScaleOnSpawn
        {
            // Dropping a second object re-enters OnCreateNew in the same frame; without this the
            // spill would multiply its own output again.
            private static bool spilling;

            // OnCreateNew is static, so the first argument is injected positionally: __instance is
            // null here and using it silently skipped every drop.
            private static void Postfix(ItemDrop __0)
            {
                if (spilling) return;
                if (Time.frameCount != _pendingFrame) return;
                if (_pendingMultiplier <= 1f) return;
                if (__0 == null || __0.m_itemData == null) return;

                int before = __0.m_itemData.m_stack;
                int total = Scale(before, _pendingMultiplier);
                int limit = MaxStack(__0);
                if (limit <= 0 || total <= limit)
                {
                    __0.m_itemData.m_stack = total;
                    Diag.TraceOnce("yield:spawn:" + _pendingMultiplier + ":" + before,
                        "[Yield]", $"stack {before} -> {total} (x{_pendingMultiplier:0.##})");
                    return;
                }

                __0.m_itemData.m_stack = limit;
                Spill(__0, total - limit, limit);
                Diag.TraceOnce("yield:spill:" + _pendingMultiplier + ":" + before,
                    "[Yield]", $"{before} x{_pendingMultiplier:0.##} = {total}: stack {limit} + {total - limit} spilled");
            }

            /// <summary>
            /// Emits the remainder as extra drops with legal stacks. DropItem is used rather than
            /// Instantiate because only it creates the ZDO the world needs to own the object.
            /// </summary>
            private static void Spill(ItemDrop source, int remaining, int limit)
            {
                var data = source.m_itemData;
                if (data == null || data.m_dropPrefab == null)
                {
                    // Without a drop prefab the remainder cannot be spawned; keep it on the stack
                    // rather than silently deleting items.
                    source.m_itemData.m_stack = limit + remaining;
                    Diag.Error("[Yield]", "spill skipped: drop prefab missing");
                    return;
                }

                spilling = true;
                try
                {
                    Vector3 origin = source.transform.position;
                    for (int guard = 0; remaining > 0 && guard < 64; guard++)
                    {
                        int amount = Mathf.Min(limit, remaining);
                        Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.4f;
                        ItemDrop.DropItem(data, amount, origin + new Vector3(offset.x, 0.1f, offset.y),
                            Quaternion.identity);
                        remaining -= amount;
                    }
                }
                catch (Exception ex)
                {
                    Diag.Error("[Yield]", "spill failed: " + ex.Message);
                }
                finally
                {
                    spilling = false;
                }
            }
        }

        // ---- pickables drop one object per item, so the stack is the thing to scale ----------
        //
        // Scaling the RPC's bonus argument only covered m_amount; a pickable with a bonus table
        // (flint hands out 1 + 2) kept its extra drops at vanilla size, so 3x gave 5 items instead
        // of 9. Drop() is the single point every pickable item passes through, main or bonus, so the
        // stack is scaled there and the count stays vanilla.

        [HarmonyPatch(typeof(Pickable), "Drop")]
        private static class ScalePickableDrop
        {
            private static void Prefix(ref int stack)
            {
                if (!Switches.Resources || stack <= 0) return;
                float multiplier = PickableMultiplier.Value;
                if (multiplier <= 1f) return;
                stack = Scale(stack, multiplier);
            }
        }
    }
}
