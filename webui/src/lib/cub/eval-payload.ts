/**
 * Сборка JS-тела для `terminal_eval` — того, что выполнится на устройстве Лампы
 * и запустит раздачу.
 *
 * Что делает готовый код на ТВ (всё — вызовы самой Лампы, реверснуты из
 * app.min.js):
 *   1. imdb → tmdb через `Lampa.TMDB.get('/find/<imdb>?external_source=imdb_id')`;
 *   2. открывает карточку `Lampa.Activity.push({component:'full', ...})`;
 *   3. добавляет magnet в локальный TorrServer `Lampa.Torserver.add({link:<magnet>})`.
 *
 * Если imdb-кода у раздачи нет — карточку открыть не по чему, поэтому запасной
 * путь просто добавляет раздачу в TorrServer без карточки (плеер откроется по
 * раздаче, без красивой карточки фильма).
 *
 * БЕЗОПАСНОСТЬ ПОДСТАНОВКИ. Тело уходит в `eval` на устройстве, поэтому magnet,
 * название и код нельзя вставлять в строку конкатенацией — кавычка в названии
 * сломала бы код или позволила инъекцию. Все значения кладём через
 * `JSON.stringify`, который даёт корректный экранированный JS-литерал.
 */

export type LampaLaunch = {
  /** magnet-ссылка раздачи — по ней играет TorrServer устройства. */
  magnet: string
  /** Название раздачи для TorrServer. */
  title: string
  /** Код IMDb (`tt…`) для открытия карточки. Без него — запуск без карточки. */
  imdb?: string | null
  /** Год — помогает Лампе выбрать нужную вещь, если find вернёт несколько. */
  year?: number | null
}

/** JS-строковый литерал из значения — безопасно экранированный. */
function lit(value: string | number | null | undefined): string {
  return JSON.stringify(value ?? '')
}

/**
 * Собирает тело для terminal_eval. Возвращает строку JS, готовую к отправке
 * в `terminalEval`.
 */
export function buildLaunchEval(launch: LampaLaunch): string {
  const magnet = lit(launch.magnet)
  const title = lit(launch.title)
  const imdb = (launch.imdb || '').trim()

  // TorrServer.add — точный формат Лампы (link = magnet). data.lampa:true
  // помечает, что запись создана Лампой (так делает её собственный код).
  const addTorrent = (movieExpr: string) => `
    Lampa.Torserver.add({
      title: ${title},
      link: ${magnet},
      poster: (${movieExpr} && ${movieExpr}.poster_path) || '',
      data: { lampa: true, movie: ${movieExpr} || {} }
    }, function () {});`

  if (!imdb) {
    // Нет кода — без карточки, только запуск раздачи.
    return `(function(){ try {${addTorrent('null')}\n} catch(e){ console.log('JacBlack launch', e); } })();`
  }

  // Есть imdb — открыть карточку и запустить раздачу.
  return `(function(){
    try {
      Lampa.TMDB.get('/find/' + ${lit(imdb)} + '?external_source=imdb_id', {}, function (d) {
        try {
          var movies = (d && d.movie_results) || [];
          var tvs = (d && d.tv_results) || [];
          var isMovie = movies.length > 0;
          var m = isMovie ? movies[0] : (tvs[0] || null);
          if (m) {
            Lampa.Activity.push({
              component: 'full',
              id: m.id,
              source: 'tmdb',
              method: isMovie ? 'movie' : 'tv',
              card: m
            });
          }
${addTorrent('m')}
        } catch (e) { console.log('JacBlack launch inner', e); }
      });
    } catch (e) { console.log('JacBlack launch', e); }
  })();`
}
