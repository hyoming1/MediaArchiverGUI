using System;

namespace MediaArchiver.Models
{
    /// <summary>
    /// 미디어 파일 하나의 분석 결과.
    /// 메타데이터 추출 후 불변(immutable)으로 취급합니다.
    /// </summary>
    public class MediaFileInfo
    {
        public string    OriginalPath { get; init; } = "";
        public DateTime? CapturedAt   { get; init; }   // KST 보정 완료
        public string?   CameraModel  { get; init; }
        public bool      IsVideo      { get; init; }
        public string    Extension    { get; init; } = ""; // 소문자, 점 없음

        public bool HasMetadata => CapturedAt.HasValue;
    }
}
