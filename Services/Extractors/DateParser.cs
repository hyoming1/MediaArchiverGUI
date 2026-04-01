using System;
using System.Text.RegularExpressions;

namespace MediaArchiver.Services.Extractors
{
    internal static class DateParser
    {
        private static readonly Regex DatePattern = new Regex(
            @"(\d{4})[:\-\./]\s*(\d{2})[:\-\./]\s*(\d{2})[T\s_]+(\d{2})[:\-\.]\s*(\d{2})[:\-\.]\s*(\d{2})(?:\.(\d{1,4}))?",
            RegexOptions.Compiled);

        public static DateTime? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var cleanRaw = raw.Trim();

            var match = DatePattern.Match(cleanRaw);
            if (match.Success)
            {
                try
                {
                    int year = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int day = int.Parse(match.Groups[3].Value);
                    int hour = int.Parse(match.Groups[4].Value);
                    int minute = int.Parse(match.Groups[5].Value);
                    int second = int.Parse(match.Groups[6].Value);

                    if (hour == 24) hour = 0;

                    // 1. 기본 날짜 생성
                    var dt = new DateTime(year, month, day, hour, minute, second);

                    // 2. ⭐ [최적화 핵심] 소수점 연산을 정수 Ticks 연산으로 대체
                    if (match.Groups[7].Success)
                    {
                        string msPart = match.Groups[7].Value;
                        int fractionalDigits = msPart.Length;
                        long val = int.Parse(msPart);

                        // 자릿수에 따라 Ticks(1/10,000,000초)로 즉시 변환
                        // 1자리(.3)   -> 3 * 1,000,000 Ticks
                        // 2자리(.35)  -> 35 * 100,000 Ticks
                        // 3자리(.351) -> 351 * 10,000 Ticks
                        // 4자리(.3512)-> 3512 * 1,000 Ticks
                        long multiplier = fractionalDigits switch
                        {
                            1 => 1_000_000,
                            2 => 100_000,
                            3 => 10_000,
                            4 => 1_000,
                            _ => 0
                        };

                        if (multiplier > 0)
                        {
                            // 무거운 AddSeconds(double) 대신 정수 덧셈만 수행
                            return new DateTime(dt.Ticks + (val * multiplier));
                        }
                    }

                    return dt;
                }
                catch { }
            }

            if (DateTime.TryParse(cleanRaw, out var fallbackDt))
                return fallbackDt;

            return null;
        }
    }
}
