using System.Collections.Generic;

namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Гигиена magnet-ссылок на выдаче. Правится здесь, а не при индексации:
    /// изменения применяются ко всей базе сразу и не требуют переиндексации.
    /// </summary>
    public class MagnetSettings
    {
        /// <summary>Вообще трогать ли ссылки перед отдачей клиенту.</summary>
        public bool enable { get; set; } = true;

        /// <summary>
        /// Трекеры, которые вырезаются из ссылок. По умолчанию — внутрисетевой
        /// ретрекер провайдера: в базе он прописан у 180 тысяч раздач и за пределами
        /// сети провайдера бесполезен, клиент просто долбится в несуществующий адрес.
        /// Сопоставление по подстроке, регистр не важен.
        /// </summary>
        public List<string> stripTrackers { get; set; } = new List<string>
        {
            "retracker.local"
        };

        /// <summary>
        /// Дописывать трекеры тем раздачам, у которых их не осталось.
        /// Больнее всего это бьёт по kinozal и nnmclub — у них 262 тысячи ссылок
        /// вообще без списка трекеров, то есть живущих на одном лишь DHT.
        /// </summary>
        public bool addDefaultTrackers { get; set; } = true;

        /// <summary>
        /// Проверенные публичные UDP-трекеры. Список отвечающих сверялся 2026-07-26.
        /// Они же используются для опроса живых сидов.
        /// </summary>
        public List<string> defaultTrackers { get; set; } = new List<string>
        {
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.demonii.com:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://opentor.net:6969/announce"
        };
    }
}
