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

        /// <summary>Когда запись последний раз проверялась. В UTC, как createTime и updateTime.</summary>
        public DateTime checkTime { get; set; } = DateTime.UtcNow;

        public string magnet { get; set; }


        public string name { get; set; }

        public string originalname { get; set; }

        public int relased { get; set; }

        /// <summary>
        /// Код IMDB вида tt0816692, если источник его сообщил.
        ///
        /// Зачем. Лампа работает с TMDB и умеет спрашивать раздачи по коду, но до
        /// сих пор код превращался в название через ЧУЖОЙ сервис, к которому нет
        /// токена — то есть поиск по коду не работал вовсе. Источники, которые
        /// отдают код вместе с названием (eztv, yts), позволяют собрать этот
        /// словарь из собственной базы и обойтись без посредника.
        ///
        /// У русских трекеров кода нет, и это нормально: достаточно, чтобы хоть
        /// одна раздача того же фильма принесла код — название найдётся по нему,
        /// а дальше поиск идёт как обычно, по названию.
        /// </summary>
        public string imdb { get; set; }


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
