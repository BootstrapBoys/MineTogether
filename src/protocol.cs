namespace MineTogether
{
    /// <summary>
    /// Every ZDO key and routed RPC name in one place. The call sites used to carry the same
    /// literals two or three times each, which is how a key drifts between the writer and the
    /// reader without anything failing to compile.
    /// </summary>
    internal static class Protocol
    {
        internal const string Active = "MineTogether.active.v1";
        internal const string Ledger = "MineTogether.ledger.v1";
        internal const string Progress = "MineTogether.progress.v1";
        internal const string FactorPrefix = "MineTogether.factor.";

        internal const string Start = "MineTogether.Start.v1";
        internal const string Stop = "MineTogether.Stop.v1";
        internal const string Hit = "MineTogether.Hit.v1";
        internal const string Reply = "MineTogether.Reply.v1";
        internal const string Keep = "MineTogether.Keep.v1";
        internal const string Manual = "MineTogether.Manual.v1";
        internal const string Move = "MineTogether.Move.v1";

        internal const string SyncRequest = "MineTogether.SyncRequest.v1";
        internal const string SyncApply = "MineTogether.SyncApply.v1";

        internal const int SyncVersion = 5;

        internal const string MinersKey = "MineTogether.miners";
        internal const string NamesKey = "MineTogether.names";
        internal const string SpeedKey = "MineTogether.speed";
        internal const string ActivityTimeKey = "MineTogether.activityTime";
    }
}
