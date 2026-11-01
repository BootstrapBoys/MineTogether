using System.Collections;
using UnityEngine;

namespace MineTogether
{
    /// <summary>
    /// Loot from a finished deposit spawns where the piece stood. When the last pieces were buried,
    /// that point is inside the ground and the stacks are unreachable - the player sees "fully
    /// mined" and no ore. After completion, any item drop near the deposit that sits below the
    /// terrain is moved up to the surface.
    ///
    /// Server only, and only on the completion path: normal mining, drops the player dropped
    /// deliberately, and anything above ground are left exactly where they are.
    /// </summary>
    internal static class DropLift
    {
        private const float Radius = 30f;
        private const float Clearance = 0.4f;

        private static int _groundMask = -1;

        internal static void Lift(Vector3 centre)
        {
            if (GameRefs.ItemDropInstances == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            if (!(GameRefs.ItemDropInstances.GetValue(null) is IList drops))
            {
                return;
            }
            if (_groundMask < 0)
            {
                _groundMask = LayerMask.GetMask("terrain", "static_solid");
            }

            int lifted = 0;
            for (int i = 0; i < drops.Count; i++)
            {
                ItemDrop drop = drops[i] as ItemDrop;
                if (drop == null) continue;

                Vector3 position = drop.transform.position;
                if ((position - centre).sqrMagnitude > Radius * Radius) continue;
                if (!TrySurface(position, out Vector3 surface)) continue;
                if (position.y >= surface.y - 0.25f) continue;

                drop.transform.position = surface + Vector3.up * Clearance;
                lifted++;
            }

            if (lifted > 0)
            {
                Diag.Trace("[Drops]", $"lifted {lifted} buried stack(s) to the surface");
            }
        }

        /// <summary>Terrain height directly above the drop, so the stack lands on the surface.</summary>
        private static bool TrySurface(Vector3 position, out Vector3 surface)
        {
            surface = position;
            Vector3 from = new Vector3(position.x, position.y + 200f, position.z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 400f, _groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            surface = hit.point;
            return true;
        }
    }
}
