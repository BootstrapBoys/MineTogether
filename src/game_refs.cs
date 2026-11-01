using System;
using System.Reflection;
using HarmonyLib;

namespace MineTogether
{
    /// <summary>
    /// Every game member this mod reaches by name, resolved once at first use.
    ///
    /// Reflection was previously resolved at each call site, including two lookups inside the
    /// melee-hit path and a per-piece lookup while a deposit is being finished. Those are
    /// stable for the lifetime of the process, so they belong in one place: a rename after a
    /// game patch shows up as a single logged failure instead of a per-frame cost.
    /// </summary>
    internal static class GameRefs
    {
        // MineRock5
        internal static readonly MethodInfo MineRock5GetAreaIndex = ResolveMethod(typeof(MineRock5), "GetAreaIndex");
        internal static readonly MethodInfo MineRock5DamageArea = ResolveMethod(typeof(MineRock5), "DamageArea");
        internal static readonly MethodInfo MineRock5RpcDamage = ResolveMethod(typeof(MineRock5), "RPC_Damage");
        internal static readonly MethodInfo MineRock5LoadHealth = ResolveMethod(typeof(MineRock5), "LoadHealth");
        internal static readonly MethodInfo MineRock5Damage = ResolveMethod(typeof(MineRock5), "Damage");
        internal static readonly FieldInfo MineRock5HitAreas = ResolveField(typeof(MineRock5), "m_hitAreas");

        // MineRock
        internal static readonly MethodInfo MineRockDamage = ResolveMethod(typeof(MineRock), "Damage");
        internal static readonly MethodInfo MineRockRpcHit = ResolveMethod(typeof(MineRock), "RPC_Hit");
        internal static readonly FieldInfo MineRockHitAreas = ResolveField(typeof(MineRock), "m_hitAreas");

        // Destructible
        internal static readonly MethodInfo DestructibleDamage = ResolveMethod(typeof(Destructible), "Damage");
        internal static readonly MethodInfo DestructibleRpcDamage = ResolveMethod(typeof(Destructible), "RPC_Damage");

        // MineRock5+HitArea / MineRock5+BoundData (nested, private)
        internal static readonly Type HitAreaType = AccessTools.Inner(typeof(MineRock5), "HitArea")!;
        internal static readonly Type BoundDataType = AccessTools.Inner(typeof(MineRock5), "BoundData")!;
        internal static readonly FieldInfo HitAreaCollider = ResolveField(HitAreaType, "m_collider");
        internal static readonly FieldInfo HitAreaHealth = ResolveField(HitAreaType, "m_health");
        internal static readonly FieldInfo HitAreaBound = ResolveField(HitAreaType, "m_bound");
        internal static readonly FieldInfo BoundPos = ResolveField(BoundDataType, "m_pos");
        internal static readonly FieldInfo BoundRot = ResolveField(BoundDataType, "m_rot");
        internal static readonly FieldInfo BoundSize = ResolveField(BoundDataType, "m_size");

        // ItemDrop keeps a private static list of every live drop; used to rescue loot that
        // spawned inside the ground when a buried piece broke.
        internal static readonly FieldInfo ItemDropInstances = ResolveField(typeof(ItemDrop), "s_instances");

        // misc networking
        internal static readonly MethodInfo GetServerPeerId = ResolveMethod(typeof(ZRoutedRpc), "GetServerPeerID");
        internal static readonly MethodInfo CreateObject = ResolveMethod(typeof(ZNetScene), "CreateObject");

        private static MethodInfo ResolveMethod(Type type, string name)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null) Plugin.Instance?.Log.LogError($"[refs] missing method {type.FullName}.{name}");
            return method!;
        }

        private static FieldInfo ResolveField(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) Plugin.Instance?.Log.LogError($"[refs] missing field {type?.FullName}.{name}");
            return field!;
        }
    }
}
