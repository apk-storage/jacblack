namespace JacRed.Models.AppConf
{
    /// <summary>
    /// У LostFilm есть своя особенность, которой нет у остальных: страница
    /// с раздачами не отдаёт magnet, за ним нужно идти на отдельную страницу
    /// каждой серии — до трёх запросов на серию.
    /// </summary>
    public class LostfilmSettings : TrackerSettings
    {
        public LostfilmSettings(string host, bool useproxy = false, LoginSettings login = null, int reqMinute = 8)
            : base(host, useproxy, login, reqMinute)
        {
        }

        /// <summary>
        /// Сколько дней доверять уже собранным раздачам серии и не ходить за
        /// ними снова. Лента `/new/` меняется медленно, поэтому при каждом
        /// прогоне в неё попадают те же серии.
        ///
        /// Ноль выключает повторное использование — тогда каждая серия
        /// запрашивается заново, как было раньше. Окно нужно затем, что список
        /// качеств со временем пополняется: 2160p появляется позже 1080p.
        /// </summary>
        public int expandReuseDays { get; set; } = 7;
    }
}
