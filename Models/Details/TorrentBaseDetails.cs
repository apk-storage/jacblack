using JacRed.Models.Tracks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace JacRed.Models.Details
{
    public class TorrentBaseDetails
    {
        public string trackerName { get; set; }

        public string[] types { get; set; }

        public string url { get; set; }


        public string title { get; set; }

        public int sid { get; set; }

        public int pir { get; set; }

        public string sizeName { get; set; }

        public DateTime createTime { get; set; } = DateTime.UtcNow;

        public DateTime updateTime { get; set; } = DateTime.UtcNow;

        public DateTime checkTime { get; set; } = DateTime.Now;

        public string magnet { get; set; }


        public string name { get; set; }

        public string originalname { get; set; }

        public int relased { get; set; }


        public HashSet<string> languages { get; set; }

        public List<ffStream> ffprobe { get; set; }

        public int ffprobe_tryingdata { get; set; }

        /// <summary>
        /// Сколько проверок подряд трекеры показали ноль сидов. Сбрасывается при
        /// первом же признаке жизни. Если трекер не ответил вовсе — счётчик не
        /// трогается: молчание означает «не знаю», а не «раздача мертва».
        /// Ноль не сериализуется, чтобы не раздувать миллион существующих записей.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int deadChecks { get; set; }

        /// <summary>Когда живость проверялась в последний раз.</summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public DateTime? lastAliveCheck { get; set; }


        public string _sn { get; set; }

        public string _so { get; set; }
    }
}
