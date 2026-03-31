using System;
using System.Diagnostics;
using System.IO;

namespace MediaArchiver.Services.Locators
{
    /// <summary>
    /// Windows 전용 ExifTool 위치 전략.
    ///
    /// 탐색 우선순위:
    ///   ① 실행 파일 옆  tools\exiftool.exe   (동봉 배포)
    ///   ② PATH 환경변수에 등록된 exiftool.exe  (시스템 설치)
    /// </summary>
    public class WindowsExifToolLocator : IExifToolLocator
    {
        private string? _cachedPath;

        public string Locate()
        {
            if (_cachedPath != null) return _cachedPath;

            // ① 로컬 동봉 경로
            var local = Path.Combine(AppContext.BaseDirectory, "tools", "exiftool.exe");
            if (File.Exists(local))
                return _cachedPath = local;

            // ② PATH 탐색
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir.Trim(), "exiftool.exe");
                if (File.Exists(candidate))
                    return _cachedPath = candidate;
            }

            return _cachedPath = "exiftool.exe";
        }

        public bool IsAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo(Locate(), "-ver")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3_000);
                return proc?.ExitCode == 0;
            }
            catch { return false; }
        }
    }

    /*
     * ── 향후 크로스플랫폼 전환 시 이 파일 옆에 추가합니다. ──────────────
     *
     * public class UnixExifToolLocator : IExifToolLocator
     * {
     *     // macOS: brew install exiftool
     *     // Linux: sudo apt install libimage-exiftool-perl
     *     public string Locate() => "exiftool";
     *
     *     public bool IsAvailable()
     *     {
     *         try {
     *             var psi = new ProcessStartInfo("exiftool", "-ver")
     *                 { RedirectStandardOutput = true, UseShellExecute = false };
     *             using var p = Process.Start(psi);
     *             p?.WaitForExit(3_000);
     *             return p?.ExitCode == 0;
     *         } catch { return false; }
     *     }
     * }
     */
}
