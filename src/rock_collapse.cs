using System.Collections;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// Makes mined multi-part deposits collapse once the part of them that actually rests on
    /// the ground is gone. An independent implementation of the behaviour PlsCollapse provides
    /// (that mod ships no licence, so none of its code is used).
    ///
    /// Vanilla already marks pieces supported/unsupported in <c>UpdateSupport</c>, but a piece
    /// hanging under another surviving piece still counts as supported, which is why veins keep
    /// floating. This module measures the rock's <em>footing</em> instead: the volume of live
    /// pieces whose support box touches terrain. The best footing ever seen is remembered, and
    /// once the surviving footing falls under the configured percentage the remaining pieces are
    /// broken through the game's own damage path so they drop normally.
    /// </summary>
    internal static class RockCollapse
    {
        internal static ConfigEntry<int> PercentFootingNeeded;
        internal static ConfigEntry<int> PercentSupportBoxSize;

        private static readonly Collider[] Buffer = new Collider[64];
        private static readonly ConditionalWeakTable<MineRock5, StrongBox<float>> Baseline =
            new ConditionalWeakTable<MineRock5, StrongBox<float>>();

        private static int _groundMask = -1;

        internal static bool Ready =>
            GameRefs.MineRock5HitAreas != null && GameRefs.HitAreaHealth != null &&
            GameRefs.HitAreaBound != null && GameRefs.BoundPos != null &&
            GameRefs.BoundRot != null && GameRefs.BoundSize != null &&
            GameRefs.MineRock5RpcDamage != null;

        internal static void Bind(ConfigFile config)
        {
            const string section = "Server Settings";
            PercentFootingNeeded = config.Bind(section, "PercentFootingNeeded", 15,
                new ConfigDescription("How much of a rock's original footing must survive; mine past that and the " +
                    "whole rock drops. 0 turns the rule off.",
                    new AcceptableValueRange<int>(0, 90)));
            PercentSupportBoxSize = config.Bind(section, "PercentSupportBoxSize", 100,
                new ConfigDescription("Size of each piece's support box relative to the piece. Lower makes pieces " +
                    "fall sooner.",
                    new AcceptableValueRange<int>(25, 150)));
        }

        [HarmonyPatch(typeof(MineRock5), "DamageArea")]
        private static class AfterPieceBroken
        {
            private static void Postfix(MineRock5 __instance)
            {
                if (!Switches.Collapse || !Ready) return;
                if (PercentFootingNeeded.Value <= 0) return;
                if (__instance == null) return;
                // The vein-mining completion loop breaks every remaining piece itself; checking
                // footing in the middle of that would break pieces twice and corrupt its ledger.
                if (Plugin.Completing) return;

                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;

                Check(__instance);
            }
        }

        private static void Check(MineRock5 rock)
        {
            if (GameRefs.MineRock5HitAreas.GetValue(rock) is not IList areas) return;

            float total = 0f;
            float grounded = 0f;
            float scale = Mathf.Clamp(PercentSupportBoxSize.Value, 25, 150) / 100f;

            for (int i = 0; i < areas.Count; i++)
            {
                object area = areas[i];
                if (area == null) continue;
                if ((float)GameRefs.HitAreaHealth.GetValue(area) <= 0f) continue;

                object bound = GameRefs.HitAreaBound.GetValue(area);
                if (bound == null) continue;

                Vector3 local = (Vector3)GameRefs.BoundPos.GetValue(bound);
                Quaternion rotation = (Quaternion)GameRefs.BoundRot.GetValue(bound);
                Vector3 size = (Vector3)GameRefs.BoundSize.GetValue(bound) * scale;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f) continue;

                float volume = size.x * size.y * size.z;
                total += volume;
                if (TouchesGround(rock.transform.position + local, size, rotation)) grounded += volume;
            }

            if (total <= 0f) return;

            // Remember the best footing ever seen on this rock. Measuring after the first piece
            // is already gone can only under-estimate it, which makes the rule fire later, never
            // earlier - the safe direction for a collapse feature.
            if (!Baseline.TryGetValue(rock, out StrongBox<float> box))
            {
                box = new StrongBox<float>(grounded);
                Baseline.Add(rock, box);
            }
            else if (grounded > box.Value)
            {
                box.Value = grounded;
            }

            float required = box.Value * (PercentFootingNeeded.Value / 100f);
            Diag.TraceOnce("footing:" + rock.GetInstanceID() + ":" + (int)grounded,
                "[Collapse]", $"{rock.m_name}: footing {grounded:0.#}/{box.Value:0.#} (needs {required:0.#}), " +
                              $"{PercentFootingNeeded.Value}%");

            if (box.Value > 0f && grounded < required)
            {
                BreakRemaining(rock, areas);
            }
        }

        private static bool TouchesGround(Vector3 position, Vector3 size, Quaternion rotation)
        {
            if (_groundMask < 0) _groundMask = LayerMask.GetMask("terrain", "static_solid");
            int hits = Physics.OverlapBoxNonAlloc(position, size, Buffer, rotation, _groundMask);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = Buffer[i];
                if (collider == null) continue;
                if (collider.GetComponentInParent<IDestructible>() is MineRock5) continue;
                return true;
            }
            return false;
        }

        /// <summary>Break every surviving piece through the game's own damage path, so drops,
        /// effects and ZDO bookkeeping stay vanilla.</summary>
        private static void BreakRemaining(MineRock5 rock, IList areas)
        {
            int broken = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                object area = areas[i];
                if (area == null) continue;
                float health = (float)GameRefs.HitAreaHealth.GetValue(area);
                if (health <= 0f) continue;

                HitData hit = new HitData
                {
                    m_point = rock.transform.position,
                    m_dir = Vector3.down,
                    m_toolTier = short.MaxValue,
                };
                hit.m_damage = new HitData.DamageTypes { m_damage = health + 1f };

                GameRefs.MineRock5RpcDamage.Invoke(rock, new object[] { 0L, hit, i });
                broken++;
            }

            if (broken > 0)
            {
                Diag.Collapses++;
                Diag.Trace("[Collapse]", $"{rock.m_name}: broke {broken} unsupported pieces");
            }
        }
    }
}
