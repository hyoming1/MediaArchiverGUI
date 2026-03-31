using System.IO;
using MediaArchiver.Infrastructure;
using MediaArchiver.Models;
using MediaArchiver.Services.Extractors;
using MediaArchiver.Services.Locators;

namespace MediaArchiver.Services
{
    /// <summary>
    /// 메타데이터 추출 폴백 체인을 조율합니다.
    ///
    ///   1순위  MetadataExtractorReader  (고속, 순수 .NET)
    ///      └─ null 반환 시 → 2순위
    ///   2순위  ExifToolReader           (저속, 범용 호환)
    ///      └─ null 반환 시 → 수동 확인 대상 (HasMetadata = false)
    /// </summary>
    public class MetadataService
    {
        private readonly IMetadataExtractor  _primary;
        private readonly IMetadataExtractor? _fallback;
        private readonly ILogger             _logger;

        // 통계 (읽기 전용 프로퍼티로 ViewModel에 노출)
        public int PrimaryHits  { get; private set; }
        public int FallbackHits { get; private set; }
        public int Misses       { get; private set; }

        public MetadataService(IExifToolLocator locator, ILogger logger)
        {
            _logger  = logger;
            _primary = new MetadataExtractorReader();

            if (locator.IsAvailable())
            {
                _fallback = new ExifToolReader(locator);
                _logger.Log(LogLevel.Info, "ExifTool 감지됨 → 폴백 활성화");
            }
            else
            {
                _logger.Log(LogLevel.Warn,
                    "ExifTool 미감지 → 폴백 비활성. " +
                    "tools\\exiftool.exe 를 배치하거나 PATH 에 등록하세요.");
            }
        }

        public MediaFileInfo Extract(string filePath)
        {
            // 1순위
            var result = _primary.TryExtract(filePath);
            if (result != null) { PrimaryHits++; return result; }

            // 2순위
            if (_fallback != null)
            {
                result = _fallback.TryExtract(filePath);
                if (result != null)
                {
                    FallbackHits++;
                    _logger.Log(LogLevel.Warn, $"[ExifTool 폴백] {Path.GetFileName(filePath)}");
                    return result;
                }
            }

            // 실패 → 수동 확인
            Misses++;
            return new MediaFileInfo
            {
                OriginalPath = filePath,
                Extension    = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            };
        }
    }
}
