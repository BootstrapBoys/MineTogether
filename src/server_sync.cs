using System;
using BepInEx.Configuration;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// The master switch and everything under [Server Settings] belong to the host, so a client
    /// cannot run different rules from the server it is playing on. Clients ask once on join and the
    /// host re-broadcasts whenever one of those values changes. Client-local settings (the personal
    /// toggle, AOE tuning, hotkey) are deliberately not synced.
    /// </summary>
    internal static class ServerSync
    {
        private const float RetrySeconds = 2f;

        private static float _nextRequest;
        private static bool _synced;

        /// <summary>Client side: ask for the host's values until one arrives.</summary>
        internal static void Tick()
        {
            if (_synced || Server.IsServer || ZRoutedRpc.instance == null)
            {
                return;
            }
            if (Time.time < _nextRequest)
            {
                return;
            }
            _nextRequest = Time.time + RetrySeconds;
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(Protocol.SyncRequest, new object[1] { new ZPackage() });
            }
            catch (Exception e)
            {
                // Not connected yet; the retry covers it, so this is a trace rather than an error.
                Diag.TraceOnce("sync:send", "[Sync]", "server not reachable yet: " + e.Message);
            }
        }

        /// <summary>Server side: answer one client.</summary>
        internal static void OnRequest(long sender, ZPackage pkg)
        {
            if (!Server.IsServer)
            {
                return;
            }
            Send(sender);
        }

        /// <summary>Server side: push the current values to everyone, after a local change.</summary>
        internal static void Broadcast()
        {
            if (!Server.IsServer || ZRoutedRpc.instance == null)
            {
                return;
            }
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Protocol.SyncApply,
                    new object[1] { Package() });
            }
            catch (Exception e)
            {
                Diag.Error("[Sync]", e);
            }
        }

        /// <summary>Client side: accept the host's values.</summary>
        internal static void Apply(long sender, ZPackage pkg)
        {
            if (Server.IsServer || Plugin.Instance == null)
            {
                return;
            }
            try
            {
                // Only the host may overwrite these, and only with a package we understand.
                if (sender != Server.ServerId)
                {
                    Diag.SyncRejected++;
                    Diag.Trace("[Sync]", $"ignored settings from peer {sender}");
                    return;
                }
                if (pkg.ReadInt() != Protocol.SyncVersion)
                {
                    Diag.SyncRejected++;
                    Diag.Trace("[Sync]", "ignored settings from an incompatible build");
                    return;
                }
                bool enabled = pkg.ReadBool();
                string additional = pkg.ReadString();
                string excluded = pkg.ReadString();
                float range = pkg.ReadSingle();
                float speed = pkg.ReadSingle();
                float playerRange = pkg.ReadSingle();
                float mining = pkg.ReadSingle();
                float lumber = pkg.ReadSingle();
                float pickable = pkg.ReadSingle();
                float structure = pkg.ReadSingle();
                int footing = pkg.ReadInt();
                int supportBox = pkg.ReadInt();
                bool scaleWithSkill = pkg.ReadBool();
                float radiusMin = pkg.ReadSingle();
                float radiusMax = pkg.ReadSingle();
                float bonusDamage = pkg.ReadSingle();
                int maxTargets = pkg.ReadInt();
                string aoeExcluded = pkg.ReadString();

                Plugin plugin = Plugin.Instance;
                Set(plugin.Enabled, enabled);
                Set(plugin.Included, additional ?? "");
                Set(plugin.Excluded, excluded ?? "");
                Set(plugin.Range, range);
                Set(plugin.SpeedPerPlayer, speed);
                Set(plugin.SpeedRange, playerRange);
                Set(ResourceYield.MiningMultiplier, mining);
                Set(ResourceYield.LumberMultiplier, lumber);
                Set(ResourceYield.PickableMultiplier, pickable);
                Set(ResourceYield.StructureMultiplier, structure);
                Set(RockCollapse.PercentFootingNeeded, footing);
                Set(RockCollapse.PercentSupportBoxSize, supportBox);
                Set(AoeMining.ScaleWithSkill, scaleWithSkill);
                Set(AoeMining.RadiusMin, radiusMin);
                Set(AoeMining.RadiusMax, radiusMax);
                Set(AoeMining.BonusDamage, bonusDamage);
                Set(AoeMining.MaxTargets, maxTargets);
                Set(AoeMining.AoeExcludedPrefabs, aoeExcluded ?? "");
                _synced = true;
                Diag.SyncApplied++;
                Diag.Trace("[Sync]", $"host settings applied: master={enabled} range={range:0.#} " +
                                     $"speed={speed:0.#}+{playerRange:0.#}m yield x{mining:0.#}/{lumber:0.#}/{pickable:0.#}/{structure:0.#}");
            }
            catch (Exception e)
            {
                // A short or stale package is not worth acting on, but it is worth recording.
                Diag.Error("[Sync]", e);
            }
        }

        /// <summary>Re-arm for the next server, so a new host's rules are fetched again.</summary>
        internal static void Reset()
        {
            _synced = false;
            _nextRequest = 0f;
        }

        private static void Send(long peer)
        {
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(peer, Protocol.SyncApply, new object[1] { Package() });
            }
            catch (Exception e)
            {
                Diag.Error("[Sync]", e);
            }
        }

        private static ZPackage Package()
        {
            Plugin plugin = Plugin.Instance;
            ZPackage pkg = new ZPackage();
            pkg.Write(Protocol.SyncVersion);
            pkg.Write(plugin.Enabled.Value);
            pkg.Write(plugin.Included.Value ?? "");
            pkg.Write(plugin.Excluded.Value ?? "");
            pkg.Write(plugin.Range.Value);
            pkg.Write(plugin.SpeedPerPlayer.Value);
            pkg.Write(plugin.SpeedRange.Value);
            pkg.Write(ResourceYield.MiningMultiplier.Value);
            pkg.Write(ResourceYield.LumberMultiplier.Value);
            pkg.Write(ResourceYield.PickableMultiplier.Value);
            pkg.Write(ResourceYield.StructureMultiplier.Value);
            pkg.Write(RockCollapse.PercentFootingNeeded.Value);
            pkg.Write(RockCollapse.PercentSupportBoxSize.Value);
            pkg.Write(AoeMining.ScaleWithSkill.Value);
            pkg.Write(AoeMining.RadiusMin.Value);
            pkg.Write(AoeMining.RadiusMax.Value);
            pkg.Write(AoeMining.BonusDamage.Value);
            pkg.Write(AoeMining.MaxTargets.Value);
            pkg.Write(AoeMining.AoeExcludedPrefabs.Value ?? "");
            return pkg;
        }

        private static void Set<T>(ConfigEntry<T> entry, T value)
        {
            if (!Equals(entry.Value, value))
            {
                entry.Value = value;
            }
        }
    }
}
