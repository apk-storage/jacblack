using JacBlack.Application.Dev;
using Microsoft.AspNetCore.Mvc;

namespace JacBlack.Controllers.Dev
{
    [Route("/dev/[action]")]
    public class DevMigrationController : Controller
    {
        readonly IDevMigrationService _migrationService;
        readonly Infrastructure.Trackers.Kinozal.KinozalSyncService _kinozal;

        public DevMigrationController(
            IDevMigrationService migrationService,
            Infrastructure.Trackers.Kinozal.KinozalSyncService kinozal)
        {
            _migrationService = migrationService;
            _kinozal = kinozal;
        }

        /// <summary>
        /// Наполнить словарь кодов Кинопоиска впрок, обойдя базу.
        ///
        /// Возвращается сразу — работа идёт в фоне, следить за ней по
        /// /dev/KinopoiskHarvestState или по счётчику в /stats/quality.
        /// Не начинает, пока идёт обход kinozal.
        /// </summary>
        public JsonResult HarvestKinopoisk(int limit = 300, int delayMs = 1500) =>
            Json(Infrastructure.Trackers.Kinozal.KinopoiskDictionaryHarvester.Start(_kinozal, limit, delayMs));

        /// <summary>Что делает наполнитель словаря прямо сейчас.</summary>
        public JsonResult KinopoiskHarvestState() =>
            Json(Infrastructure.Trackers.Kinozal.KinopoiskDictionaryHarvester.Snapshot());

        public JsonResult FixKnabenNames() => Json(_migrationService.FixKnabenNames());

        public JsonResult FixBitruNames() => Json(_migrationService.FixBitruNames());

        public JsonResult RemoveNullValues() => Json(_migrationService.RemoveNullValues());

        public JsonResult RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null) =>
            Json(_migrationService.RemoveBucket(key, migrateName, migrateOriginalname));

        public JsonResult FixEmptySearchFields() => Json(_migrationService.FixEmptySearchFields());

        public JsonResult MigrateAnilibertyUrls() => Json(_migrationService.MigrateAnilibertyUrls());

        public JsonResult RemoveDuplicateAniliberty() => Json(_migrationService.RemoveDuplicateAniliberty());

        public JsonResult FixAnimelayerDuplicates() => Json(_migrationService.FixAnimelayerDuplicates());

        public JsonResult FixAnimeToshoNames() => Json(_migrationService.FixAnimeToshoNames());

        public JsonResult FixAnimeToshoUrls() => Json(_migrationService.FixAnimeToshoUrls());

        /// <summary>
        /// Слияние записей, задвоившихся при смене домена трекера.
        /// Сначала запускать с `?dryRun=true` — посчитает, ничего не трогая.
        /// </summary>
        public JsonResult FixDomainDuplicates(bool dryRun = true) => Json(_migrationService.FixDomainDuplicates(dryRun));

        /// <summary>Чистка того, чего нет в TMDB (спорт и прочее). Сначала ?dryRun=true.</summary>
        public JsonResult RemoveNonTmdbContent(bool dryRun = true) => Json(_migrationService.RemoveNonTmdbContent(dryRun));

        /// <summary>Единая нормализация пробелов в именах. Сначала ?dryRun=true.</summary>
        public JsonResult NormalizeWhitespace(bool dryRun = true) => Json(_migrationService.NormalizeWhitespace(dryRun));

        /// <summary>Уборка осиротевших файлов шардов. Сначала ?dryRun=true.</summary>
        public JsonResult RemoveOrphanShards(bool dryRun = true) => Json(_migrationService.RemoveOrphanShards(dryRun));

        /// <summary>Проставить код IMDB по названию и году. Сначала ?dryRun=true.</summary>
        public JsonResult FillImdbFromDictionary(bool dryRun = true) => Json(_migrationService.FillImdbFromDictionary(dryRun));

        /// <summary>
        /// Проставить код Кинопоиска по названию и году. Нужен русскому кино,
        /// у которого кода IMDB нет ни на одном трекере. Сначала ?dryRun=true.
        /// </summary>
        public JsonResult FillKinopoiskFromDictionary(bool dryRun = true) => Json(_migrationService.FillKinopoiskFromDictionary(dryRun));

        /// <summary>
        /// Восстановить год из заголовка у записей, где разбор его потерял.
        /// Нужен потому, что год в запросе теперь жёсткое условие отбора,
        /// и запись без года в карточку с годом не попадает. Сначала ?dryRun=true.
        /// </summary>
        public JsonResult FixMissingYear(bool dryRun = true) => Json(_migrationService.FixMissingYear(dryRun));

        /// <summary>
        /// Наполнить словарь всеми написаниями названия, чтобы поиск переводил
        /// запрос: «Веном» дотягивался до английских раздач yts и наоборот.
        /// Записи не меняются, только словарь. Сначала ?dryRun=true.
        /// </summary>
        public JsonResult RebuildImdbAka(bool dryRun = true) => Json(_migrationService.RebuildImdbAka(dryRun));
    }
}
