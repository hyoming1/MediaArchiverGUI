using System;
using System.Text.RegularExpressions;

namespace MediaArchiver.Services.Extractors
{
    internal static class DateParser
    {
        // 년(4) [기호] 월(2) [기호] 일(2) [공백/T] 시(2) [기호] 분(2) [기호] 초(2) 패턴을 강제로 찾아냅니다.
        private static readonly Regex DatePattern = new Regex(
            @"(\d{4})[:\-\./]\s*(\d{2})[:\-\./]\s*(\d{2})[T\s_]+(\d{2})[:\-\.]\s*(\d{2})[:\-\.]\s*(\d{2})",
            RegexOptions.Compiled);

        public static DateTime? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var cleanRaw = raw.Trim();

            // 1. 정규식으로 년,월,일,시,분,초 숫자만 완벽하게 추출
            var match = DatePattern.Match(cleanRaw);
            if (match.Success)
            {
                try
                {
                    return new DateTime(
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value),
                        int.Parse(match.Groups[5].Value),
                        int.Parse(match.Groups[6].Value)
                    );
                }
                catch { /* 날짜 형태가 유효하지 않으면 무시하고 아래 폴백으로 이동 */ }
            }

            // 2. 만약 정규식이 실패하면 C# 기본 파서로 마지막 시도
            if (DateTime.TryParse(cleanRaw, out var dt))
                return dt;

            return null;
        }
    }
}