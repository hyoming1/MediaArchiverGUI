using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaArchiver.Models;

namespace MediaArchiver.Services.Extractors
{
    public class MetadataExtractorReader : IMetadataExtractor
    {
        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase)
                { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".mts" };

        public MediaFileInfo? TryExtract(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (VideoExtensions.Contains(ext))
            {
                return null;
            }

            try
            {
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);

                // ⭐ 파서에 넘기기 전, 문자열을 담아둘 임시 변수들
                string? dateStr = null;
                string? subSecStr = null;
                string? model = null;

                foreach (var dir in directories)
                {
                    if (model == null)
                    {
                        var tag = dir.Tags.FirstOrDefault(t =>
                            t.Name.Equals("Model", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Equals("Camera Model Name", StringComparison.OrdinalIgnoreCase));
                        if (tag?.Description != null)
                            model = tag.Description;
                    }

                    // 1. 기본 촬영 시간 가져오기 ("2026:02:01 05:16:02")
                    if (dateStr == null)
                    {
                        var tag = dir.Tags.FirstOrDefault(t =>
                            t.Name.Equals("Date/Time Original", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Equals("Date/Time", StringComparison.OrdinalIgnoreCase));

                        if (tag?.Description != null)
                            dateStr = tag.Description;
                    }

                    // ⭐ 2. 밀리초 데이터 가져오기 ("351" 등)
                    // 라이브러리나 카메라마다 표기법이 조금씩 달라서 IndexOf로 넉넉하게 잡습니다.
                    if (subSecStr == null)
                    {
                        var tag = dir.Tags.FirstOrDefault(t =>
                            t.Name.IndexOf("Sub-Sec Time Original", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Sub-Sec Time", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Sub Sec Time", StringComparison.OrdinalIgnoreCase) >= 0);

                        if (tag?.Description != null)
                            subSecStr = tag.Description;
                    }
                }

                if (dateStr == null) return null;

                // ⭐ 3. 날짜와 밀리초를 점(.)으로 합쳐줍니다.
                string rawForParser = dateStr;
                if (!string.IsNullOrWhiteSpace(subSecStr))
                {
                    rawForParser = $"{dateStr}.{subSecStr}";
                }

                // 4. 합쳐진 완벽한 문자열을 파서에 넘깁니다.
                DateTime? rawDate = DateParser.Parse(rawForParser);

                if (rawDate == null) return null;

                return new MediaFileInfo
                {
                    OriginalPath = filePath,
                    IsVideo = false,
                    Extension = ext.TrimStart('.'),
                    CapturedAt = rawDate.Value,
                    // ⭐ 모델명 못 찾으면 null 처리
                    CameraModel = model != null ? ModelSanitizer.Sanitize(model) : null,
                };
            }
            catch { return null; }
        }
    }
}