namespace MediaArchiver.Models
{
    /// <summary>
    /// 아카이브 진행 상황 스냅샷.
    /// OnProgress 콜백을 통해 UI 레이어로 전달됩니다.
    /// </summary>
    public class ArchiveProgress
    {
        public int      TotalFiles     { get; init; }
        public int      ProcessedFiles { get; init; }
        public string   CurrentFile    { get; init; } = "";
        public string   Message        { get; init; } = "";
        public LogLevel Level          { get; init; }

        public double Percentage =>
            TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
    }
}
