using BepInEx.Configuration;
using ConditionalConfigSync;

namespace MineTogether
{
    /// <summary>
    /// Conditional Config Sync wiring. Every entry is registered with an explicit ownership mode,
    /// so the question "may a client change this?" is answered at the bind site instead of being
    /// implied by which module reads it.
    ///
    /// <c>Host</c> entries are the server's: they decide mining speed, yields, collapse and the
    /// area effect, and a client cannot run different numbers. <c>Local</c> entries are the
    /// player's own: the personal toggle, its hotkey and the log switch.
    /// </summary>
    internal static class ServerConfig
    {
        internal static readonly ConfigSync Sync = new ConfigSync(Plugin.Id)
        {
            DisplayName = "Mine Together",
            CurrentVersion = Plugin.Version,
            MinimumRequiredVersion = Plugin.Version,
            ModRequired = true
        };

        /// <summary>Server-owned: synced from the host and locked for clients.</summary>
        internal static ConfigEntry<T> Host<T>(ConfigEntry<T> entry)
        {
            Sync.AddConfigEntry(entry, ConfigSyncMode.AlwaysServerControlled);
            return entry;
        }

        /// <summary>Client-owned: kept out of the sync, editable per player.</summary>
        internal static ConfigEntry<T> Local<T>(ConfigEntry<T> entry)
        {
            Sync.AddConfigEntry(entry, ConfigSyncMode.AlwaysClientControlled);
            return entry;
        }
    }
}
