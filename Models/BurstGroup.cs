using System;
using System.Collections.Generic;

namespace MediaArchiver.Models
{
    /// <summary>
    /// 동일 초(second)에 촬영된 연사 파일 그룹.
    /// IsBurst == true 이면 모든 파일에 _001, _002 … 순번이 강제 부여됩니다.
    /// </summary>
    public class BurstGroup
    {
        public DateTime            Key   { get; init; }
        public List<MediaFileInfo> Files { get; init; } = new();

        public bool IsBurst => Files.Count > 1;
    }
}
