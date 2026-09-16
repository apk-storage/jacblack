namespace JacBlack.Models.AppConf
{
    public class TrackerSettings
    {
        public TrackerSettings(string host, bool useproxy = false, LoginSettings login = null, int reqMinute = 8)
        {
            this.host = host;
            this.useproxy = useproxy;
            this.reqMinute = reqMinute;

            if (login != null)
                this.login = login;
        }


        public string host { get; }

        public string alias { get; set; }

        public string rqHost(string uri = null)
        {
            if (uri == null)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    return alias;

                return host;
            }

            if (string.IsNullOrWhiteSpace(alias))
                return uri;

            return uri.Replace(host, alias);
        }


        public string cookie { get; set; }

        /// <summary>When true and logParsers is enabled, parser writes to Data/log/{tracker}.log</summary>
        public bool log { get; set; } = true;

        public bool useproxy { get; set; }

        public int reqMinute { get; set; }

        public int parseDelay
        {
            get
            {
                if (reqMinute == -1)
                    return 10;

                if (reqMinute <= 0)
                    return 60_000;

                // Потолок в 100 мс: быстрее пауза уже ничего не сдерживает, а
                // опечатка в конфиге не превратится в шквал запросов.
                //
                // Раньше при reqMinute >= 60 пауза жёстко равнялась секунде, и
                // разогнаться выше шестидесяти страниц в минуту было нельзя. У
                // rutracker 16.09.2026 эта секунда стала заметной: страница в
                // браузере занимает 2,5 с, обходной браузер занят 78% времени,
                // а остальное — наша пауза. Для reqMinute <= 60 значение не
                // меняется: (60/reqMinute)*1000 и 60000/reqMinute совпадают.
                return System.Math.Max(100, 60_000 / reqMinute);
            }
        }

        public LoginSettings login { get; set; } = new LoginSettings();
    }
}
