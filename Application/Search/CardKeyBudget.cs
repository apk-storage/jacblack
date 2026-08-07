using System;
using System.Collections.Generic;

namespace JacBlack.Application.Search
{
    /// <summary>
    /// Делит потолок на чтение файлов между двумя названиями карточки.
    ///
    /// Поиск по карточке собирает ключи дважды — по русскому названию и по
    /// оригинальному, — а читать можно не больше `maxreadfile` файлов. Раньше
    /// оба набора сваливались в один HashSet и обрезались одним `Take`. Порядок
    /// обхода HashSet не определён, поэтому обрезка выкидывала произвольный
    /// кусок, и редкая сторона исчезала целиком.
    ///
    /// Кому это било по рукам: зарубежным источникам. У раздачи с piratebay,
    /// yts или eztv русского названия нет вовсе, оба поля латиницей — такие
    /// записи лежат ТОЛЬКО под ключом оригинального названия. Стоило русскому
    /// набору занять весь потолок, и они пропадали. Замер 07.08.2026 на
    /// карточке «Дюна / Dune» 2021: по русскому 117 раздач, по оригинальному
    /// 125 (из них 8 зарубежных), а вместе — 60 и ни одной зарубежной.
    /// Объединение множеств меньше каждого из них быть не может; это и был
    /// след обрезки.
    ///
    /// Правило простое: меньшему набору забронирована ПОЛОВИНА потолка. Если
    /// он меньше половины — проходит целиком (обычный случай: зарубежных
    /// раздач у карточки заметно меньше), остаток добирает больший. Если оба
    /// набора велики — каждому ровно половина, и ни один не исчезает.
    ///
    /// Число читаемых файлов не растёт, то есть скорость прежняя: меняется не
    /// сколько читаем, а что именно.
    /// </summary>
    public static class CardKeyBudget
    {
        public static HashSet<string> Split(
            IReadOnlyList<string> byName,
            IReadOnlyList<string> byOriginal,
            int cap)
        {
            var result = new HashSet<string>(Math.Max(4, cap));

            if (cap <= 0)
                return result;

            var a = byName ?? (IReadOnlyList<string>)Array.Empty<string>();
            var b = byOriginal ?? (IReadOnlyList<string>)Array.Empty<string>();

            var small = a.Count <= b.Count ? a : b;
            var large = ReferenceEquals(small, a) ? b : a;

            // Половина потолка — бронь меньшего набора. Если он и так меньше
            // брони, проходит целиком, и ничего не пропадает.
            int reserved = Math.Min(small.Count, Math.Max(1, cap / 2));

            foreach (string k in small)
            {
                if (result.Count >= reserved)
                    break;

                result.Add(k);
            }

            foreach (string k in large)
            {
                if (result.Count >= cap)
                    break;

                result.Add(k);
            }

            // Большой набор мог кончиться раньше потолка (или целиком совпасть
            // с уже взятым) — тогда добираем остатком из меньшего, чтобы не
            // читать меньше, чем позволено.
            foreach (string k in small)
            {
                if (result.Count >= cap)
                    break;

                result.Add(k);
            }

            return result;
        }
    }
}
