using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Persistence
{
    /// <summary>
    /// Раздачи, которых на трекере больше нет.
    ///
    /// Зачем отдельным списком, а не полем записи. База шардируется по имени
    /// раздачи, и добавление поля означает переписывание шардов и миграцию
    /// ключей — дорого ради признака, который касается единиц записей.
    /// Список же занимает килобайты и переживает перезапуск.
    ///
    /// Почему вообще нужно. Удалённая раздача остаётся в базе навсегда: в
    /// поиске трекера она не появляется, опросить её нечем, и последний
    /// снимок счётчиков живёт вечно. Случай 02.08.2026: у «Кода 8» раздача
    /// t=5820892 показывала 96 раздающих, а трекер на её странице отвечает
    /// «Тема не найдена». Скачать такую нельзя — значит и показывать её в
    /// выдаче незачем.
    ///
    /// Помечаем ТОЛЬКО когда трекер сам сказал, что темы нет. Ни короткая
    /// страница, ни пустой ответ поиска доказательством не считаются: так
    /// можно похоронить живую раздачу из-за сетевого сбоя.
    ///
    /// Список обратим: достаточно удалить Data/dead.json, и всё вернётся.
    /// </summary>
    public static class DeadReleases
    {
        const string Path = "Data/dead.json";

        static readonly ConcurrentDictionary<string, DateTime> _dead = new(StringComparer.OrdinalIgnoreCase);

        static readonly object _saveLock = new object();

        static bool _dirty;

        static DeadReleases()
        {
            try
            {
                if (!File.Exists(Path))
                    return;

                var loaded = JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(Path));
                if (loaded == null)
                    return;

                foreach (var kv in loaded)
                    _dead[kv.Key] = kv.Value;

                JacRedLog.Information(JacRedLogCategories.Trackers, $"список удалённых раздач загружен: {_dead.Count}");
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, "список удалённых раздач не прочитан", ex);
            }
        }

        public static int Count => _dead.Count;

        static string Key(string tracker, string id) => $"{tracker}:{id}";

        public static bool IsDead(string tracker, string id) =>
            !string.IsNullOrWhiteSpace(id) && _dead.ContainsKey(Key(tracker, id));

        public static void Remember(string tracker, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (_dead.TryAdd(Key(tracker, id), DateTime.Now))
            {
                _dirty = true;
                Save();
            }
        }

        static void Save()
        {
            lock (_saveLock)
            {
                if (!_dirty)
                    return;

                try
                {
                    Directory.CreateDirectory("Data");
                    File.WriteAllText(Path, JsonConvert.SerializeObject(_dead));
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    JacRedLog.Swallowed(JacRedLogCategories.Trackers, "список удалённых раздач не сохранён", ex);
                }
            }
        }
    }
}
