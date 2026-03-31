using System;

namespace MediaArchiver.Models
{
    /// <summary>아카이브 실행 결과 요약</summary>
    public class ArchiveResult
    {
        public int      TotalScanned     { get; set; }
        public int      SuccessCount     { get; set; }
        public int      ManualCheckCount { get; set; }
        public int      FailureCount     { get; set; }
        public int      FallbackCount    { get; set; }
        public TimeSpan Elapsed          { get; set; }
    }
}
