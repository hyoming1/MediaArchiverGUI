using MediaArchiver.Models;

namespace MediaArchiver.Services.Extractors
{
    /// <summary>
    /// 단일 파일에서 MediaFileInfo 를 추출하는 추출기 계약.
    /// null 반환 = 추출 실패 → 폴백 또는 수동 확인 처리.
    /// </summary>
    public interface IMetadataExtractor
    {
        MediaFileInfo? TryExtract(string filePath);
    }
}
