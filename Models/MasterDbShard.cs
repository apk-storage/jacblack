using System;

namespace JacBlack.Models
{
    /// <summary>Per-shard metadata in masterDb (update time + file ordering key).</summary>
    public class MasterDbShard
    {
        public DateTime updateTime { get; set; }

        public long fileTime { get; set; }
    }
}
