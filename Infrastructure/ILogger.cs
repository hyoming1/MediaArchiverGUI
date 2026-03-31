namespace MediaArchiver.Infrastructure
{
    /// <summary>
    /// 로그 출력 추상화.
    /// 구현체: ViewModels/MainViewModel 내부의 GuiLogger (GUI),
    ///         ConsoleLogger (콘솔 버전 유지 시 사용 가능).
    /// </summary>
    public interface ILogger
    {
        void Log(LogLevel level, string message);
    }
}
