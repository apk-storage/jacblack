using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Models.Tracks;

namespace JacRed.Infrastructure.Parsing
{
    /// <summary>
    /// Сводка дорожек для выдачи поиска: кодек картинки, кодеки звука и,
    /// если есть чем, разбор по отдельным дорожкам.
    ///
    /// Данных два источника, и они очень разного качества. ffprobe знает
    /// правду — кодек, язык и число каналов каждой дорожки, — но есть далеко
    /// не у всех: замер 31.07.2026 по трём запросам дал 0 из 431, 15 из 257 и
    /// 29 из 94. Заголовок есть всегда, но из него видно лишь НАБОР кодеков,
    /// без привязки к дорожкам.
    ///
    /// Поэтому связку «эта студия озвучила в этом кодеке» из заголовка мы не
    /// выводим: её там нет, и придумывать её значило бы показывать
    /// правдоподобную ложь. Пары отдаём только из ffprobe, из заголовка —
    /// отдельными наборами.
    /// </summary>
    public static class MediaTracks
    {
        public sealed class AudioTrack
        {
            public string codec { get; set; }
            public string language { get; set; }
            public int? channels { get; set; }
            public string title { get; set; }
        }

        public sealed class Summary
        {
            /// <summary>Кодек картинки: x265, x264, av1, xvid.</summary>
            public string video { get; set; }

            /// <summary>Кодеки звука без привязки к дорожкам — из заголовка.</summary>
            public string[] audio { get; set; }

            /// <summary>Разбор по дорожкам — только когда есть ffprobe.</summary>
            public List<AudioTrack> tracks { get; set; }

            /// <summary>Языки субтитров — только когда есть ffprobe.</summary>
            public string[] subtitles { get; set; }

            public bool IsEmpty =>
                string.IsNullOrEmpty(video)
                && (audio == null || audio.Length == 0)
                && (tracks == null || tracks.Count == 0)
                && (subtitles == null || subtitles.Length == 0);
        }

        // Синонимы сводим к одному написанию: в заголовках встречаются и HEVC,
        // и h.265, и x265 — для человека это одно и то же, и три разные плашки
        // вместо одной только мешали бы.
        static readonly (Regex Pattern, string Name)[] VideoCodecs =
        {
            (new Regex(@"\b(x\.?265|h\.?265|hevc)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "x265"),
            (new Regex(@"\b(x\.?264|h\.?264|avc)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "x264"),
            (new Regex(@"\bav1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "av1"),
            (new Regex(@"\b(xvid|divx)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "xvid"),
            (new Regex(@"\bmpeg-?2\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "mpeg2")
        };

        // Порядок важен: DTS-HD должен опознаться раньше, чем DTS, иначе
        // длинное имя потеряется. То же у E-AC3 против AC3.
        static readonly (Regex Pattern, string Name)[] AudioCodecs =
        {
            (new Regex(@"\bdts[\s._-]*(hd|x|ma)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "dts-hd"),
            (new Regex(@"\bdts\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "dts"),
            // Хвостовой границы слова тут быть не должно: в заголовках пишут
            // «DDP5.1», где сразу за ddp идёт цифра и никакой границы нет.
            // Тест на это есть — он и поймал ошибку.
            (new Regex(@"(\be[\s._-]?ac3\b|\beac3\b|\bddp\d*|\bdd\+)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "eac3"),
            (new Regex(@"\b(ac3|dd5\.1|dolby[\s._-]?digital)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ac3"),
            (new Regex(@"\btrue[\s._-]?hd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "truehd"),
            (new Regex(@"\batmos\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "atmos"),
            (new Regex(@"\bflac\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "flac"),
            (new Regex(@"\baac\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "aac"),
            (new Regex(@"\bopus\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "opus"),
            (new Regex(@"\bmp3\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "mp3")
        };

        public static Summary Build(IReadOnlyList<ffStream> ffprobe, string title)
        {
            var summary = new Summary
            {
                video = VideoFromTitle(title),
                audio = AudioFromTitle(title)
            };

            if (ffprobe == null || ffprobe.Count == 0)
                return summary;

            var tracks = new List<AudioTrack>();
            var subtitles = new List<string>();

            foreach (var s in ffprobe)
            {
                if (s == null || string.IsNullOrEmpty(s.codec_type))
                    continue;

                if (s.codec_type.Equals("video", StringComparison.OrdinalIgnoreCase))
                {
                    // ffprobe знает точнее заголовка — его ответ и берём.
                    string v = NormalizeVideo(s.codec_name);
                    if (!string.IsNullOrEmpty(v))
                        summary.video = v;
                    continue;
                }

                if (s.codec_type.Equals("audio", StringComparison.OrdinalIgnoreCase))
                {
                    tracks.Add(new AudioTrack
                    {
                        codec = NormalizeAudio(s.codec_name),
                        language = Language(s.tags?.language),
                        channels = s.channels,
                        title = string.IsNullOrWhiteSpace(s.tags?.title) ? null : s.tags.title.Trim()
                    });
                    continue;
                }

                if (s.codec_type.Equals("subtitle", StringComparison.OrdinalIgnoreCase))
                {
                    string lang = Language(s.tags?.language);
                    if (!string.IsNullOrEmpty(lang) && !subtitles.Contains(lang))
                        subtitles.Add(lang);
                }
            }

            if (tracks.Count > 0)
            {
                summary.tracks = tracks;

                // Раз есть разбор по дорожкам, набор из заголовка становится
                // лишним: он говорит то же самое, но грубее.
                var fromTracks = tracks.Select(t => t.codec).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToArray();
                if (fromTracks.Length > 0)
                    summary.audio = fromTracks;
            }

            if (subtitles.Count > 0)
                summary.subtitles = subtitles.ToArray();

            return summary;
        }

        public static string VideoFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            foreach (var (pattern, name) in VideoCodecs)
                if (pattern.IsMatch(title))
                    return name;

            return null;
        }

        public static string[] AudioFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            var found = new List<string>();

            foreach (var (pattern, name) in AudioCodecs)
                if (pattern.IsMatch(title) && !found.Contains(name))
                    found.Add(name);

            // dts-hd поглощает dts: показывать обе плашки бессмысленно.
            if (found.Contains("dts-hd"))
                found.Remove("dts");

            if (found.Contains("eac3"))
                found.Remove("ac3");

            return found.Count == 0 ? null : found.ToArray();
        }

        static string NormalizeVideo(string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return null;

            switch (codec.Trim().ToLowerInvariant())
            {
                case "hevc": return "x265";
                case "h265": return "x265";
                case "h264": return "x264";
                case "avc": return "x264";
                case "av1": return "av1";
                case "mpeg4": return "xvid";
                case "mpeg2video": return "mpeg2";
                default: return codec.Trim().ToLowerInvariant();
            }
        }

        static string NormalizeAudio(string codec)
        {
            if (string.IsNullOrWhiteSpace(codec))
                return null;

            switch (codec.Trim().ToLowerInvariant())
            {
                case "eac3": return "eac3";
                case "ac3": return "ac3";
                case "dts": return "dts";
                case "truehd": return "truehd";
                default: return codec.Trim().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Код языка приводим к двум буквам: ffprobe отдаёт и «rus», и «ru»,
        /// а на карточке это должна быть одна плашка, а не две.
        /// </summary>
        static string Language(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            string c = code.Trim().ToLowerInvariant();

            switch (c)
            {
                case "rus": case "ru": return "ru";
                case "eng": case "en": return "en";
                case "ukr": case "uk": return "uk";
                case "jpn": case "ja": return "ja";
                case "fra": case "fre": case "fr": return "fr";
                case "deu": case "ger": case "de": return "de";
                case "spa": case "es": return "es";
                case "ita": case "it": return "it";
                case "kor": case "ko": return "ko";
                case "chi": case "zho": case "zh": return "zh";
                // Трёхбуквенные коды нельзя обрезать до двух: por → pt, а не po.
                // Без явного перечня на карточке выходил разнобой — часть языков
                // двумя буквами, часть тремя.
                case "heb": case "he": return "he";
                case "por": case "pt": return "pt";
                case "pol": case "pl": return "pl";
                case "ces": case "cze": case "cs": return "cs";
                case "slk": case "slo": case "sk": return "sk";
                case "tur": case "tr": return "tr";
                case "ara": case "ar": return "ar";
                case "hin": case "hi": return "hi";
                case "nld": case "dut": case "nl": return "nl";
                case "swe": case "sv": return "sv";
                case "nor": case "no": return "no";
                case "dan": case "da": return "da";
                case "fin": case "fi": return "fi";
                case "ell": case "gre": case "el": return "el";
                case "hun": case "hu": return "hu";
                case "ron": case "rum": case "ro": return "ro";
                case "bul": case "bg": return "bg";
                case "srp": case "sr": return "sr";
                case "hrv": case "hr": return "hr";
                case "tha": case "th": return "th";
                case "vie": case "vi": return "vi";
                case "ind": case "id": return "id";
                case "fas": case "per": case "fa": return "fa";
                case "und": case "unknown": case "": return null;
                default: return c.Length == 2 ? c : null;
            }
        }
    }
}
