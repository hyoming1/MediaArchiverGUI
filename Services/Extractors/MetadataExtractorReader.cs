using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaArchiver.Models;

namespace MediaArchiver.Services.Extractors
{
    public class MetadataExtractorReader : IMetadataExtractor
    {
        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase)
                { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".wmv", ".mts" };

        public MediaFileInfo? TryExtract(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (VideoExtensions.Contains(ext))
            {
                return null;
            }

            try
            {
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
                DateTime? rawDate = null;
                string? model = null;

                foreach (var dir in directories)
                {
                    if (model == null)
                    {
                        var tag = dir.Tags.FirstOrDefault(t =>
                            t.Name.Equals("Model", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Equals("Camera Model Name", StringComparison.OrdinalIgnoreCase));
                        if (tag?.Description != null)
                            model = tag.Description;
                    }

                    if (rawDate == null)
                    {
                        var tag =
                            dir.Tags.FirstOrDefault(t => t.Name.Equals("Date/Time Original", StringComparison.OrdinalIgnoreCase)) ??
                            dir.Tags.FirstOrDefault(t => t.Name.Equals("Date/Time", StringComparison.OrdinalIgnoreCase));

                        if (tag?.Description != null)
                            rawDate = DateParser.Parse(tag.Description);
                    }
                }

                if (rawDate == null) return null;

                return new MediaFileInfo
                {
                    OriginalPath = filePath,
                    IsVideo = false,
                    Extension = ext.TrimStart('.'),
                    CapturedAt = rawDate.Value,
                    // ⭐ 모델명 못 찾으면 null 처리
                    CameraModel = model != null ? ModelSanitizer.Sanitize(model) : null,
                };
            }
            catch { return null; }
        }
    }
}