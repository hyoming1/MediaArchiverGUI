using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MediaArchiver.Services.Extractors
{
    /// <summary>
    /// 카메라 모델명에서 파일명 불가 문자를 치환하고, 공백을 제거하는 유틸리티.
    /// </summary>
    internal static class ModelSanitizer
    {
        private static readonly HashSet<char> InvalidChars =
            new(Path.GetInvalidFileNameChars());

        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unknown";

            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                // 공백(띄어쓰기)은 완전히 제거 (건너뜀)
                if (c == ' ') continue;

                // 윈도우 파일명 불가 문자(\, /, :, *, ?, ", <, >, |)는 언더바로 치환
                if (InvalidChars.Contains(c))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Trim('_');
        }
    }
}