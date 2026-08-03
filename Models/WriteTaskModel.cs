using JacRed.Infrastructure.Persistence;
using System;

namespace JacRed.Models
{
    public class WriteTaskModel
    {
        public FileDB db { get; set; }

        public DateTime lastread { get; set; }

        /// <summary>
        /// Время создания записи, в UTC — как и <see cref="lastread"/>. Раньше
        /// здесь стояло местное время: в контейнере оно на два часа впереди, и
        /// две даты одной записи жили в разных часах. Уборка кеша сравнивала то
        /// одну, то другую, и достаточно было перепутать их местами, чтобы
        /// шарды либо вытеснялись на два часа раньше срока, либо не
        /// вытеснялись вовсе.
        /// </summary>
        public DateTime create { get; set; } = DateTime.UtcNow;

        public int countread { get; set; }

        public int openconnection { get; set; }
    }
}
