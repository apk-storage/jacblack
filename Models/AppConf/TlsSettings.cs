using System;
using System.Collections.Generic;
using System.Linq;

namespace JacBlack.Models.AppConf
{
    /// <summary>
    /// Проверка TLS-сертификатов исходящих запросов.
    ///
    /// Раньше она была отключена жёстко и для всех: `ServerCertificateCustomValidationCallback`
    /// всегда возвращал true. Это касалось и запросов с логином и паролем трекера,
    /// то есть подмена сервера прошла бы незамеченной.
    ///
    /// Теперь проверка включена, а исключения задаются явным списком — чтобы они
    /// были видны в конфиге, а не спрятаны в коде.
    /// </summary>
    public class TlsSettings
    {
        /// <summary>Проверять цепочку сертификатов. Выключать целиком не рекомендуется.</summary>
        public bool validate { get; set; } = true;

        /// <summary>
        /// Хосты, для которых проверка пропускается. По состоянию на 27.07.2026
        /// такой один: torrent.by не отдаёт промежуточный сертификат, цепочка
        /// не собирается. Он к тому же не приносил новых раздач с 21.08.2024 —
        /// если решим его отключить, строку отсюда можно убрать.
        /// </summary>
        public List<string> allowInvalidFor { get; set; } = new List<string>
        {
            "torrent.by"
        };

        public bool IsAllowedInvalid(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || allowInvalidFor == null)
                return false;

            return allowInvalidFor.Any(h =>
                !string.IsNullOrWhiteSpace(h) &&
                (host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
