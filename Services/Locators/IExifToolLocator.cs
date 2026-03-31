namespace MediaArchiver.Services.Locators
{
    /// <summary>
    /// ExifTool 실행 파일 위치 전략 인터페이스.
    ///
    /// 크로스플랫폼 전환 시 이 인터페이스의 구현체만 교체하면 됩니다.
    ///   현재: WindowsExifToolLocator
    ///   예정: UnixExifToolLocator (macOS / Linux)
    /// </summary>
    public interface IExifToolLocator
    {
        /// <summary>exiftool 실행 파일의 전체 경로 또는 PATH 상 명령어 이름</summary>
        string Locate();

        /// <summary>ExifTool 실행 가능 여부</summary>
        bool IsAvailable();
    }
}
