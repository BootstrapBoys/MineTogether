namespace MineTogether
{
    /// <summary>
    /// One address for "is this module allowed to run". Every module and the debugger read the
    /// same predicates, so the master switch cannot be honoured in one code path and forgotten in
    /// another, and a state dump can describe the mod without guessing.
    ///
    /// Every read is null-safe: this is called from the diagnostics header, which is written while
    /// the config is still being bound, as well as from patch bodies afterwards.
    /// </summary>
    internal static class Switches
    {
        private static Plugin P => Plugin.Instance;

        internal static bool Master => P != null && P.Enabled != null && P.Enabled.Value;

        internal static bool VeinMine => Master && P.VeinMine != null && P.VeinMine.Value;

        internal static bool Resources => Master && P.ResourceMultiplier != null && P.ResourceMultiplier.Value;

        internal static bool Collapse => Master && P.FallingDeposits != null && P.FallingDeposits.Value;

        internal static bool Aoe => Master && P.AreaMining != null && P.AreaMining.Value;

        /// <summary>Human-readable state for the startup line and the diagnostics header.</summary>
        internal static string Describe()
        {
            return $"master={Master} vein={VeinMine} resources={Resources} collapse={Collapse} aoe={Aoe}";
        }
    }
}
