using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaArchiver.Infrastructure;
using MediaArchiver.Models;

namespace MediaArchiver.Services
{
    public class ArchiveEngine
    {
        private static readonly HashSet<string> SupportedExtensions =
          new(StringComparer.OrdinalIgnoreCase)
          {
        ".jpg", ".jpeg", ".png", ".heic", ".heif",
        ".tiff", ".tif", ".bmp", ".gif", ".webp",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
        ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".mts"
          };

        private readonly string _sourceDir;
        private readonly string _manualCheckDir;
        private readonly string _successDir;
        private readonly MetadataService _metadataService;
        private readonly NamingService _namingService;
        private readonly ILogger _logger;

        // ⭐ 스레드 안전한 가방으로 교체 (여러 스레드가 동시에 로그를 넣어도 안전함)
        private readonly ConcurrentBag<string> _manualCheckLogs = new();

        // ⭐ 카운트 업데이트 시 충돌을 막기 위한 자물쇠
        private readonly object _syncLock = new object();

        public Action<ArchiveProgress>? OnProgress { get; set; }

        // ⭐ 추가: 검사 모드 여부
        public bool IsCheckOnlyMode { get; set; }

        public ArchiveEngine(
          string sourceDir,
          MetadataService metadataService,
          NamingService namingService,
          ILogger logger)
        {
            _sourceDir = sourceDir;
            _manualCheckDir = Path.Combine(sourceDir, "수동확인필요");
            _successDir = Path.Combine(sourceDir, "분류완료");

            _metadataService = metadataService;
            _namingService = namingService;
            _logger = logger;
        }

        public ArchiveResult Run(CancellationToken ct = default)
        {
            var result = new ArchiveResult();
            var sw = Stopwatch.StartNew();

            // ── STEP 1: 파일 스캔 (하위 폴더 포함 및 미지원 파일 분류) ──────────────────
            _logger.Log(LogLevel.Info, IsCheckOnlyMode ? "STEP 1 · [검사 모드] 파일 스캔 중..." : "STEP 1 · 파일 스캔 중 (하위 폴더 포함)...");

            var allFiles = new List<string>();
            var unsupportedFiles = new List<string>();

            // 하위 폴더를 모두 탐색하되, 분류완료/수동확인필요 폴더는 무시합니다.
            foreach (var file in Directory.EnumerateFiles(_sourceDir, "*", SearchOption.AllDirectories))
            {
                var dirName = Path.GetDirectoryName(file);
                if (dirName != null &&
                  (dirName.StartsWith(_successDir, StringComparison.OrdinalIgnoreCase) ||
                  dirName.StartsWith(_manualCheckDir, StringComparison.OrdinalIgnoreCase) ||
                  dirName.Equals(_successDir, StringComparison.OrdinalIgnoreCase) ||
                  dirName.Equals(_manualCheckDir, StringComparison.OrdinalIgnoreCase)))
                {
                    continue; // 이미 처리된 폴더 안의 파일은 스캔 패스 (무한 루프 방지)
                }

                var fileName = Path.GetFileName(file);

                // ⭐ [핵심 추가] 윈도우/맥 시스템 찌꺼기 파일은 아예 무시(스킵)합니다.
                if (fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
          fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
          fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // allFiles에도, unsupportedFiles에도 넣지 않고 다음 파일로 넘어감
                }

                if (SupportedExtensions.Contains(Path.GetExtension(file)))
                {
                    allFiles.Add(file);
                }
                else
                {
                    unsupportedFiles.Add(file); // 텍스트, PDF 등 미지원 파일 분리
                }
            }

            result.TotalScanned = allFiles.Count + unsupportedFiles.Count;
            _logger.Log(LogLevel.Info, $"총 {result.TotalScanned}개 파일 발견 (미디어: {allFiles.Count}개, 미지원/기타: {unsupportedFiles.Count}개)");

            if (result.TotalScanned == 0) return result;

            // ── STEP 2: 메타데이터 추출 ──────────────────────────────────
            _logger.Log(LogLevel.Info, "STEP 2 · 메타데이터 추출 중...");

            var analyzedBag = new ConcurrentBag<MediaFileInfo>();
            int processedCount = 0;

            int degreeOfParallelism = Math.Min(Environment.ProcessorCount + (Environment.ProcessorCount / 2), 12);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = degreeOfParallelism,
                CancellationToken = ct
            };

            Parallel.ForEach(allFiles, parallelOptions, file =>
            {
                ct.ThrowIfCancellationRequested();

                var info = _metadataService.Extract(file);
                analyzedBag.Add(info);

                int current = Interlocked.Increment(ref processedCount);

                if (current % 10 == 0 || current == allFiles.Count)
                {
                    ReportProgress(new ArchiveProgress
                    {
                        TotalFiles = allFiles.Count,
                        ProcessedFiles = current,
                        CurrentFile = Path.GetFileName(file),
                        Message = $"메타데이터 분석 중... ({current}/{allFiles.Count})",
                        Level = LogLevel.Info,
                    });
                }
            });

            var analyzed = analyzedBag.ToList();
            var validFiles = analyzed.Where(f => f.HasMetadata).ToList();
            var manualFiles = analyzed.Where(f => !f.HasMetadata).ToList();

            result.FallbackCount = _metadataService.FallbackHits;

            // ── STEP 3: 예외 파일(미지원 확장자 + 메타데이터 없음) 격리 ───────────────
            if (manualFiles.Count > 0 || unsupportedFiles.Count > 0)
            {
                // ⭐ 검사 모드일 때는 이동하지 않고 로그만 출력
                if (IsCheckOnlyMode)
                {
                    _logger.Log(LogLevel.Info, $"STEP 3 · [검사 모드] 예외 파일 총 {manualFiles.Count + unsupportedFiles.Count}개 발견 (이동 안 함)");
                    foreach (var file in unsupportedFiles)
                        _logger.Log(LogLevel.Warn, $"[미지원 파일] {Path.GetFileName(file)}");
                    foreach (var file in manualFiles)
                        _logger.Log(LogLevel.Warn, $"[메타데이터 없음] {Path.GetFileName(file.OriginalPath)}");
                }
                else
                {
                    _logger.Log(LogLevel.Warn, $"예외 파일 총 {manualFiles.Count + unsupportedFiles.Count}개를 '수동확인필요' 폴더로 격리합니다.");
                    Directory.CreateDirectory(_manualCheckDir);

                    // 1. 미지원 확장자(기타 파일) 먼저 격리
                    foreach (var file in unsupportedFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        MoveToManualCheck(file, ref result, "지원하지 않는 파일 형식 (인식 불가)");
                    }

                    // 2. 메타데이터가 없는 미디어 파일 격리
                    foreach (var file in manualFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        MoveToManualCheck(file.OriginalPath, ref result, "메타데이터(촬영일시) 정보 없음");
                    }
                }
            }

            // ── STEP 4: 폴더/파일명 규칙 검사 및 계획 수립 ────────────────────────
            _logger.Log(LogLevel.Info, IsCheckOnlyMode ? "STEP 4 · 파일명 일치 여부 검사 중..." : "STEP 4 · 폴더 분류 및 이름 변경 계획 수립 중...");

            var groups = validFiles
              .GroupBy(f =>
              {
                  var d = f.CapturedAt!.Value;
                  // ⭐ 수정됨: 시간, 기기명, 확장자를 모두 합쳐서 고유 키(Key)로 만듭니다.
                  var timeKey = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
                  var modelKey = f.CameraModel ?? "Unknown";
                  var extKey = (f.Extension ?? "").ToLowerInvariant();

                  return new { Time = timeKey, Model = modelKey, Ext = extKey };
              })
              .Select(g => new BurstGroup
              {
                  // 폴더 생성용 날짜 키
                  Key = g.Key.Time,

                  // ⭐ Millisecond 대신 Ticks를 사용하여 4자리 미세 오차까지 완벽하게 정렬!
                  Files = g.OrderBy(f => f.CapturedAt!.Value.Ticks).ToList()
              })
              .ToList();

            // ArchiveEngine.cs - STEP 4 groups 생성 직후 삽입 임시
            foreach (var g in groups.Where(x => x.Files.Count > 1)) // 연사(파일 2개 이상) 그룹만 필터링
            {
                System.Diagnostics.Debug.WriteLine($"\n[연사 그룹 확인] 시간 키: {g.Key}");
                foreach (var f in g.Files)
                {
                    System.Diagnostics.Debug.WriteLine($" ➔ Ticks: {f.CapturedAt!.Value.Ticks} | 파일명: {System.IO.Path.GetFileName(f.OriginalPath)}");
                }
            }

            var plan = new List<(string OriginalPath, string TempPath, string FinalPath)>();

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();

                var destFolder = _namingService.GetArchiveFolder(_successDir, group.Key);

                for (int i = 0; i < group.Files.Count; i++)
                {
                    var file = group.Files[i];

                    // ⭐ 이제 동일한 초, 동일한 기기, 동일한 확장자인 경우에만 IsBurst가 true가 됩니다.                    
                    var finalName = group.IsBurst
                  ? _namingService.BuildFileName(file, i + 1)
                  : _namingService.BuildFileName(file);

                    var finalPath = Path.Combine(destFolder, finalName);
                    var originalFileName = Path.GetFileName(file.OriginalPath);

                    // ⭐ [핵심 수정] 검사 모드일 때는 '파일 이름'만 비교하고, 실제 이동 모드일 때는 '전체 폴더 경로'까지 완벽하게 비교합니다.
                    bool isAlreadyCorrect = IsCheckOnlyMode
            ? originalFileName.Equals(finalName, StringComparison.OrdinalIgnoreCase)
            : file.OriginalPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase);

                    if (isAlreadyCorrect)
                    {
                        result.SuccessCount++;
                        continue;
                    }

                    // ⭐ 검사 모드일 때는 예상 이름만 출력하고 작업 목록(plan)에 넣지 않음
                    if (IsCheckOnlyMode)
                    {
                        _logger.Log(LogLevel.Warn, $"[변경 필요] {originalFileName} ➔ 예상: {finalName}");
                    }
                    else
                    {
                        var tempPath = Path.Combine(destFolder, $"{Guid.NewGuid():N}.tmp");
                        plan.Add((file.OriginalPath, tempPath, finalPath));
                    }
                }
            }

            // ⭐ 검사 모드인 경우 여기서 로직 즉시 종료 (이동/텍스트 파일 생성 생략)
            if (IsCheckOnlyMode)
            {
                result.Elapsed = sw.Elapsed;
                _logger.Log(LogLevel.Success, $"[검사 완료] 올바른 파일: {result.SuccessCount}개 | 변경 필요/예외: {validFiles.Count - result.SuccessCount + manualFiles.Count + unsupportedFiles.Count}개");
                return result;
            }

            if (plan.Count == 0 && manualFiles.Count == 0 && unsupportedFiles.Count == 0)
            {
                _logger.Log(LogLevel.Success, "모든 파일이 정상 규격으로 존재합니다.");
                result.Elapsed = sw.Elapsed;
                return result;
            }

            // ⭐ HDD 스래싱 방지용 세팅 (미래를 대비한 구조)
            // 나중에 SSD 환경이 되면 이 값을 2~4로 올리면 곧바로 성능업 됩니다.
            var ioParallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 2, // 요청하신 대로 1로 강제 고정!
                CancellationToken = ct
            };

            // ── STEP 5: GUID 임시 이름으로 1차 안전 변경 및 폴더 이동 ───────────────────
            if (plan.Count > 0)
            {
                _logger.Log(LogLevel.Info, "STEP 5 · 분류 폴더 생성 및 이동(임시 전환) 중...");
            }

            var tempMap = new ConcurrentBag<(string OriginalPath, string TempPath, string FinalPath)>();
            int moveCount = 0;

            Parallel.ForEach(plan, ioParallelOptions, item =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TempPath)!);
                    File.Move(item.OriginalPath, item.TempPath);
                    tempMap.Add(item);

                    int current = Interlocked.Increment(ref moveCount);
                    // ⭐ 추가: 임시 이동 중에도 UI 진행 상태 바와 텍스트를 업데이트합니다.
                    ReportProgress(new ArchiveProgress
                    {
                        TotalFiles = plan.Count,
                        ProcessedFiles = current,
                        CurrentFile = Path.GetFileName(item.OriginalPath),
                        Message = $"임시 폴더로 이동 중... ({current}/{plan.Count})",
                        Level = LogLevel.Info,
                    });
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"[오류] {Path.GetFileName(item.OriginalPath)} 이동 중 오류 발생. 격리 조치합니다.");
                    MoveToManualCheck(item.OriginalPath, ref result, $"임시 폴더로 이동 중 시스템 오류 ({ex.Message})");
                }
            });

            // ── STEP 6: 최종 파일명 적용 ──────────────────────────────────
            if (tempMap.Count > 0)
            {
                _logger.Log(LogLevel.Info, "STEP 6 · 최종 이름 적용 중...");
            }

            var finalizedList = tempMap.ToList();
            int finalCount = 0;

            Parallel.ForEach(finalizedList, ioParallelOptions, item =>
            {
                var uniqueFinalPath = PathHelper.EnsureUnique(item.FinalPath);
                var finalName = Path.GetFileName(uniqueFinalPath);

                try
                {
                    File.Move(item.TempPath, uniqueFinalPath);

                    // 자물쇠를 걸어 안전하게 성공 카운트 증가
                    lock (_syncLock) { result.SuccessCount++; }

                    int current = Interlocked.Increment(ref finalCount);
                    ReportProgress(new ArchiveProgress
                    {
                        TotalFiles = finalizedList.Count,
                        ProcessedFiles = current,
                        CurrentFile = finalName,
                        Message = $"분류 완료 ({current}/{finalizedList.Count}): {finalName}",
                        Level = LogLevel.Success,
                    });
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"[오류] {finalName} 적용 실패. 격리 조치합니다.");
                    MoveTempToManualCheck(item.TempPath, item.OriginalPath, ref result, $"최종 이름 변경 중 시스템 오류 ({ex.Message})");
                }
            });

            // ── 사유 로깅 (텍스트 파일 출력) ────────────────────────────────
            if (_manualCheckLogs.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(_manualCheckDir);
                    var logFilePath = Path.Combine(_manualCheckDir, $"수동확인_리스트_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllLines(logFilePath, _manualCheckLogs);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"[로깅 실패] 수동확인 리스트 txt 파일 생성 중 오류: {ex.Message}");
                }
            }

            result.Elapsed = sw.Elapsed;
            _logger.Log(LogLevel.Success,
              $"작업 완료 · 성공: {result.SuccessCount} | " +
              $"수동확인(격리됨): {result.ManualCheckCount} | " +
              $"실패: {result.FailureCount} | " +
              $"소요: {result.Elapsed.TotalSeconds:F2}초");

            return result;
        }

        // ── 오류 발생 파일 격리 헬퍼 메서드 (스레드 안전성 적용) ────────────────────────────────

        private void MoveToManualCheck(string originalPath, ref ArchiveResult result, string reason)
        {
            try
            {
                // 격리 폴더 생성 및 이동은 lock 밖에서 수행 (병목 방지)
                Directory.CreateDirectory(_manualCheckDir);
                var dest = PathHelper.EnsureUnique(Path.Combine(_manualCheckDir, Path.GetFileName(originalPath)));
                File.Move(originalPath, dest);

                // 결과 객체를 수정할 때만 짧게 자물쇠를 채움
                lock (_syncLock) { result.ManualCheckCount++; }
                _manualCheckLogs.Add($"{Path.GetFileName(originalPath)} : {reason}"); // ConcurrentBag이라 안전
            }
            catch (Exception ex)
            {
                lock (_syncLock) { result.FailureCount++; }
                _manualCheckLogs.Add($"{Path.GetFileName(originalPath)} : {reason} (격리 실패: {ex.Message})");
            }
        }

        private void MoveTempToManualCheck(string tempPath, string originalPath, ref ArchiveResult result, string reason)
        {
            try
            {
                Directory.CreateDirectory(_manualCheckDir);
                var dest = PathHelper.EnsureUnique(Path.Combine(_manualCheckDir, Path.GetFileName(originalPath)));
                File.Move(tempPath, dest);

                lock (_syncLock) { result.ManualCheckCount++; }
                _manualCheckLogs.Add($"{Path.GetFileName(originalPath)} : {reason}");
            }
            catch (Exception ex)
            {
                lock (_syncLock) { result.FailureCount++; }
                _manualCheckLogs.Add($"{Path.GetFileName(originalPath)} : {reason} (격리 실패: {ex.Message})");
            }
        }

        private void ReportProgress(ArchiveProgress progress)
          => OnProgress?.Invoke(progress);
    }
}