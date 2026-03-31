using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using MediaArchiver.Models;
using MediaArchiver.Services.Locators;

namespace MediaArchiver.Services.Extractors
{
    public class ExifToolReader : IMetadataExtractor
    {
        private readonly string _exe;

        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase)
                { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".mts" };

        private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

        public ExifToolReader(IExifToolLocator locator)
            => _exe = locator.Locate();

        public MediaFileInfo? TryExtract(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isVideo = VideoExtensions.Contains(ext);

            try
            {
                // ⭐ CameraModelName, Make 태그 추가
                var args = $"-charset utf8 -json " +
                           $"-DateTimeOriginal -MediaCreateDate -CreateDate " +
                           $"-Author -Model -DeviceModelName -SamsungModel -CameraModelName -Make " +
                           $"\"{filePath}\"";

                var psi = new ProcessStartInfo(_exe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(30_000);

                int jsonStart = stdout.IndexOf('[');
                int jsonEnd = stdout.LastIndexOf(']');

                if (jsonStart == -1 || jsonEnd == -1)
                    return null;

                string cleanJson = stdout.Substring(jsonStart, jsonEnd - jsonStart + 1);

                return ParseJson(cleanJson, filePath, ext, isVideo);
            }
            catch { return null; }
        }

        private static MediaFileInfo? ParseJson(
            string json, string filePath, string ext, bool isVideo)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var entry = doc.RootElement[0];

                DateTime? rawDate = null;
                bool requiresKstOffset = false; // UTC로 저장된 동영상인지 확인용

                string[] dateKeys = isVideo
                    ? new[] { "MediaCreateDate", "CreateDate", "DateTimeOriginal" }
                    : new[] { "DateTimeOriginal", "CreateDate" };

                foreach (var key in dateKeys)
                {
                    if (entry.TryGetProperty(key, out var prop))
                    {
                        string dateStr = prop.ValueKind == JsonValueKind.String ? prop.GetString()! : prop.ToString();

                        if (dateStr.Contains("0000:00:00")) continue;

                        rawDate = DateParser.Parse(dateStr);
                        if (rawDate != null)
                        {
                            // ⭐ 아이폰/갤럭시(MP4)의 MediaCreateDate는 UTC이므로 +9시간 필요.
                            // 하지만 소니(MTS)의 DateTimeOriginal은 이미 로컬시간이므로 보정 안 함.
                            if (isVideo && (key == "MediaCreateDate" || key == "CreateDate"))
                            {
                                requiresKstOffset = true;
                            }
                            break;
                        }
                    }
                }

                if (rawDate == null) return null;

                var capturedAt = requiresKstOffset ? rawDate.Value.Add(KstOffset) : rawDate.Value;

                // ⭐ 소니 카메라 대응 (CameraModelName -> Model 순으로 확인)
                string? model = null;
                string[] modelKeys = { "Author", "CameraModelName", "Model", "DeviceModelName", "SamsungModel" };
                foreach (var mKey in modelKeys)
                {
                    if (entry.TryGetProperty(mKey, out var m) && !string.IsNullOrWhiteSpace(m.GetString()))
                    {
                        model = m.GetString()!;
                        break;
                    }
                }

                return new MediaFileInfo
                {
                    OriginalPath = filePath,
                    IsVideo = isVideo,
                    Extension = ext.TrimStart('.'),
                    CapturedAt = capturedAt,
                    CameraModel = model != null ? ModelSanitizer.Sanitize(model) : null,
                };
            }
            catch { return null; }
        }
    }
}