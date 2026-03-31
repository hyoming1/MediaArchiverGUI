using System.IO;

namespace MediaArchiver.Infrastructure
{
    /// <summary>
    /// 파일 경로 관련 유틸리티.
    /// 동일 경로가 이미 존재하면 _dup1, _dup2 … 접미사를 붙여 고유 경로를 반환합니다.
    /// </summary>
    public static class PathHelper
    {
        public static string EnsureUnique(string path)
        {
            if (!File.Exists(path)) return path;

            var dir  = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext  = Path.GetExtension(path);
            int n    = 1;
            string candidate;

            do { candidate = Path.Combine(dir, $"{name}_dup{n++}{ext}"); }
            while (File.Exists(candidate));

            return candidate;
        }
    }
}
