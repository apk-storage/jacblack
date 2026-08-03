using System;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Logging;

namespace JacBlack.Application.Dev.Migrations
{
    public abstract class DevMigrationBase
    {
        protected readonly IFastDbIndex FastDbIndex;

        protected DevMigrationBase(IFastDbIndex fastDbIndex) => FastDbIndex = fastDbIndex;

        /// <summary>
        /// Пересобирает индекс после правки базы. Сбой не отменяет саму миграцию —
        /// данные уже записаны, а индекс всё равно перестроится по расписанию.
        /// Но молчать нельзя: до следующей плановой пересборки поиск будет отдавать
        /// старую картину, и без этой строки причину не найти.
        ///
        /// Ловит здесь, поэтому оборачивать вызовы своим try не нужно.
        /// </summary>
        protected void TryRebuildFastDb()
        {
            try
            {
                FastDbIndex.Rebuild();
            }
            catch (Exception ex)
            {
                JacBlackLog.Swallowed(JacBlackLogCategories.Fdb, "индекс после миграции не пересобрался", ex);
            }
        }
    }
}
