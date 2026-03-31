using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
        private readonly string _successDir; // 분류완료 폴더 경로 추가
        private readonly MetadataService _metadataService;
        private readonly NamingService _namingService;
        private readonly ILogger _logger;

        public Action<ArchiveProgress>? OnProgress { get; set; }

        public ArchiveEngine(
            string sourceDir,
            MetadataService metadataService,
            NamingService namingService,
            ILogger logger)
        {
            _sourceDir = sourceDir;
            _manualCheckDir = Path.Combine(sourceDir, "수동확인필요");
            _successDir = Path.Combine(sourceDir, "분류완료"); // 대상 폴더 하위의 분류완료 폴더

            _metadataService = metadataService;
            _namingService = namingService;
            _logger = logger;
        }

        public ArchiveResult Run(CancellationToken ct = default)
        {
            var result = new ArchiveResult();
            var sw = Stopwatch.StartNew();

            // ── STEP 1: 파일 스캔 (하위 폴더 포함) ────────────────────────────────────────
            _logger.Log(LogLevel.Info, "STEP 1 · 파일 스캔 중 (하위 폴더 포함)...");

            var allFiles = Directory
                .EnumerateFiles(_sourceDir, "*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            result.TotalScanned = allFiles.Count;
            _logger.Log(LogLevel.Info, $"총 {allFiles.Count}개 파일 발견");

            if (allFiles.Count == 0) return result;

            // ── STEP 2: 메타데이터 추출 ──────────────────────────────────
            _logger.Log(LogLevel.Info, "STEP 2 · 메타데이터 추출 중...");

            var analyzed = new List<MediaFileInfo>(allFiles.Count);
            for (int i = 0; i < allFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var info = _metadataService.Extract(allFiles[i]);
                analyzed.Add(info);

                ReportProgress(new ArchiveProgress
                {
                    TotalFiles = allFiles.Count,
                    ProcessedFiles = i + 1,
                    CurrentFile = Path.GetFileName(allFiles[i]),
                    Message = $"메타데이터 확인 중... ({i + 1}/{allFiles.Count})",
                    Level = LogLevel.Info,
                });
            }

            var validFiles = analyzed.Where(f => f.HasMetadata).ToList();
            var manualFiles = analyzed.Where(f => !f.HasMetadata).ToList();

            result.FallbackCount = _metadataService.FallbackHits;

            // ── STEP 3: 실패 파일(메타데이터 없음) 격리 폴더로 이동 ────────────────────────
            if (manualFiles.Count > 0)
            {
                _logger.Log(LogLevel.Warn, $"STEP 3 · 메타데이터가 없는 파일 {manualFiles.Count}개를 '수동확인필요' 폴더로 격리합니다.");
                Directory.CreateDirectory(_manualCheckDir);

                foreach (var file in manualFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    if (Path.GetDirectoryName(file.OriginalPath)!.Equals(_manualCheckDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dest = PathHelper.EnsureUnique(Path.Combine(_manualCheckDir, Path.GetFileName(file.OriginalPath)));
                    try
                    {
                        File.Move(file.OriginalPath, dest);
                        result.ManualCheckCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, $"[격리 실패] {Path.GetFileName(file.OriginalPath)}: {ex.Message}");
                        result.FailureCount++;
                    }
                }
            }

            // ── STEP 4: 연사 그룹화 및 폴더/파일명 계획 수립 ────────────────────────
            _logger.Log(LogLevel.Info, "STEP 4 · 폴더 분류 및 이름 변경 계획 수립 중...");

            var groups = validFiles
                .GroupBy(f =>
                {
                    var d = f.CapturedAt!.Value;
                    return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);
                })
                .Select(g => new BurstGroup { Key = g.Key, Files = g.ToList() })
                .ToList();

            var plan = new List<(string OriginalPath, string TempPath, string FinalPath)>();

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();

                // ⭐ NamingService를 사용하여 분류완료/YYYY년/MM월 폴더 경로 생성
                var destFolder = _namingService.GetArchiveFolder(_successDir, group.Key);

                for (int i = 0; i < group.Files.Count; i++)
                {
                    var file = group.Files[i];

                    var finalName = group.IsBurst
                        ? _namingService.BuildFileName(file, i + 1)
                        : _namingService.BuildFileName(file);

                    var finalPath = Path.Combine(destFolder, finalName);

                    // 이미 정확한 폴더 구조(분류완료/년/월) 안에 정확한 이름으로 있다면 건너뛰기
                    if (file.OriginalPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SuccessCount++;
                        continue;
                    }

                    // 해당 월 폴더 안에서 임시 이름으로 변경 후 진행
                    var tempPath = Path.Combine(destFolder, $"{Guid.NewGuid():N}.tmp");
                    plan.Add((file.OriginalPath, tempPath, finalPath));
                }
            }

            if (plan.Count == 0 && manualFiles.Count == 0)
            {
                _logger.Log(LogLevel.Success, "모든 파일이 이미 분류완료 폴더에 정상 규격으로 존재합니다.");
                result.Elapsed = sw.Elapsed;
                return result;
            }

            // ── STEP 5: GUID 임시 이름으로 1차 안전 변경 및 폴더 이동 ───────────────────
            _logger.Log(LogLevel.Info, "STEP 5 · 분류 폴더 생성 및 이동(임시 전환) 중...");

            var tempMap = new List<(string OriginalPath, string TempPath, string FinalPath)>();

            for (int i = 0; i < plan.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (original, temp, final) = plan[i];

                try
                {
                    // ⭐ 이동하기 전에 분류완료/년/월 폴더가 없으면 생성
                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);

                    File.Move(original, temp);
                    tempMap.Add((original, temp, final));
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"[오류] {Path.GetFileName(original)} 이동 중 오류 발생. 격리 조치합니다.");
                    MoveToManualCheck(original, ref result);
                }
            }

            // ── STEP 6: 최종 파일명 적용 ──────────────────────────────────
            _logger.Log(LogLevel.Info, "STEP 6 · 최종 이름 적용 중...");

            for (int i = 0; i < tempMap.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (originalPath, tempPath, finalPath) = tempMap[i];
                var uniqueFinalPath = PathHelper.EnsureUnique(finalPath);
                var finalName = Path.GetFileName(uniqueFinalPath);

                try
                {
                    File.Move(tempPath, uniqueFinalPath);
                    result.SuccessCount++;

                    ReportProgress(new ArchiveProgress
                    {
                        TotalFiles = tempMap.Count,
                        ProcessedFiles = i + 1,
                        CurrentFile = finalName,
                        Message = $"분류 완료 ({i + 1}/{tempMap.Count}): {finalName}",
                        Level = LogLevel.Success,
                    });
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, $"[오류] {finalName} 적용 실패. 격리 조치합니다.");
                    MoveTempToManualCheck(tempPath, originalPath, ref result);
                }
            }

            result.Elapsed = sw.Elapsed;
            _logger.Log(LogLevel.Success,
                $"작업 완료 · 성공: {result.SuccessCount} | " +
                $"수동확인(격리됨): {result.ManualCheckCount} | " +
                $"실패: {result.FailureCount} | " +
                $"소요: {result.Elapsed.TotalSeconds:F1}초");

            return result;
        }

        // ── 오류 발생 파일 격리 헬퍼 메서드 ────────────────────────────────

        private void MoveToManualCheck(string originalPath, ref ArchiveResult result)
        {
            try
            {
                Directory.CreateDirectory(_manualCheckDir);
                var dest = PathHelper.EnsureUnique(Path.Combine(_manualCheckDir, Path.GetFileName(originalPath)));
                File.Move(originalPath, dest);
                result.ManualCheckCount++;
            }
            catch { result.FailureCount++; }
        }

        private void MoveTempToManualCheck(string tempPath, string originalPath, ref ArchiveResult result)
        {
            try
            {
                Directory.CreateDirectory(_manualCheckDir);
                var dest = PathHelper.EnsureUnique(Path.Combine(_manualCheckDir, Path.GetFileName(originalPath)));
                File.Move(tempPath, dest);
                result.ManualCheckCount++;
            }
            catch { result.FailureCount++; }
        }

        private void ReportProgress(ArchiveProgress progress)
            => OnProgress?.Invoke(progress);
    }
}