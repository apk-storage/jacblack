using System.Threading;
using JacBlack.Infrastructure.Logging;

namespace JacBlack.Infrastructure.Persistence
{
    /// <summary>
    /// Не даёт словарю затереть себя обеднённым снимком.
    ///
    /// Случай из жизни, 07.08.2026: словарь кодов Кинопоиска держал 444 записи,
    /// а при следующем старте загрузился с одной. Запись у него атомарная
    /// (временный файл плюс переименование), так что обрыв на середине ни при
    /// чём. Погубило другое сочетание: загрузка не удалась и оставила словарь
    /// ПУСТЫМ, обход добавил одну запись, и сохранение честно записало снимок
    /// из одной записи поверх четырёхсот сорока четырёх. То есть механизм,
    /// добавленный ради надёжности, и стал способом потерять данные.
    ///
    /// Отсюда два правила, и оба про одно — «не знаешь, что на диске, не пиши»:
    ///
    /// 1. Загрузка провалилась — писать нельзя вообще. Пустая память в этом
    ///    случае означает не «словарь пуст», а «мы не смогли его прочитать».
    /// 2. Снимок меньше того, что лежит на диске, — писать нельзя. Записи из
    ///    словарей не удаляются, они только добавляются, поэтому уменьшение —
    ///    всегда признак беды, а не законное изменение.
    ///
    /// Отказ громкий: беззвучно переставший сохраняться словарь неотличим от
    /// работающего, пока не потеряешь его целиком.
    /// </summary>
    public sealed class IndexWriteGuard
    {
        readonly string _name;

        /// <summary>1 — загрузка провалилась, писать запрещено.</summary>
        int _blocked;

        /// <summary>Сколько записей заведомо лежит на диске. -1 — неизвестно.</summary>
        int _diskCount = -1;

        public IndexWriteGuard(string name) => _name = name;

        /// <summary>Файла нет вовсе — это законный пустой старт, не поломка.</summary>
        public void FileMissing()
        {
            Interlocked.Exchange(ref _diskCount, 0);
            Interlocked.Exchange(ref _blocked, 0);
        }

        public void LoadSucceeded(int count)
        {
            Interlocked.Exchange(ref _diskCount, count);
            Interlocked.Exchange(ref _blocked, 0);
        }

        public void LoadFailed(string why)
        {
            Interlocked.Exchange(ref _blocked, 1);
            JacBlackLog.Warning(JacBlackLogCategories.Fdb,
                $"{_name}: загрузка не удалась ({why}) — запись запрещена, чтобы не затереть файл на диске");
        }

        /// <summary>Можно ли записать снимок такого размера.</summary>
        public bool MayWrite(int snapshotCount)
        {
            if (Volatile.Read(ref _blocked) == 1)
            {
                JacBlackLog.Warning(JacBlackLogCategories.Fdb,
                    $"{_name}: сохранение пропущено — словарь не был загружен, на диске лежит более полная копия");
                return false;
            }

            int onDisk = Volatile.Read(ref _diskCount);

            // Загрузки не было вовсе — значит мы не знаем, что лежит на диске,
            // и «неизвестно» обязано означать «не пиши». Именно здесь заслон
            // и протекал: словарь начинал пополняться раньше загрузки, снимок
            // выходил крошечным, а проверка «меньше, чем на диске» пропускала
            // его, потому что на диске значилось -1. Так словарь Кинопоиска
            // потерял 475 записей уже ПОСЛЕ того, как заслон был поставлен.
            if (onDisk < 0)
            {
                JacBlackLog.Warning(JacBlackLogCategories.Fdb,
                    $"{_name}: сохранение пропущено — словарь ещё не загружался, что на диске неизвестно");
                return false;
            }

            if (onDisk > snapshotCount)
            {
                JacBlackLog.Warning(JacBlackLogCategories.Fdb,
                    $"{_name}: сохранение пропущено — в снимке {snapshotCount} записей, а на диске {onDisk}. " +
                    "Словари только растут, значит записи потерялись в памяти, и файл трогать нельзя");
                return false;
            }

            return true;
        }

        public void WriteSucceeded(int count) => Interlocked.Exchange(ref _diskCount, count);
    }
}
