using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MediaArchiver.Models;

namespace MediaArchiver.Services
{
    public class NamingService
    {
        private static readonly Dictionary<string, string> ModelAliasMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "shw-m250s", "GalaxyS2" },
                { "shv-e210k", "GalaxyS3" },
                { "shv-e210s", "GalaxyS3" },
                { "shw-m440s", "GalaxyS3" },
                { "sm-n900k", "GalaxyNote3" },
                { "sm-g935k", "GalaxyS7Edge" },
                { "sm-g935l", "GalaxyS7Edge" },
                { "sm-g950n", "GalaxyS8" },
                { "sm-g965n", "GalaxyS9Plus" },
                { "sm-n960n", "GalaxyNote9" },
                { "sm-g998n", "GalaxyS21Ultra" },
                { "sm-s908n", "GalaxyS22Ultra" },
                { "sm-a515f", "GalaxyA51" },
                { "sm-t975n", "GalaxyTabS7Plus" },
                { "im-a840sp", "VegaS5" },
                { "im-a910k", "VegaIron2" },
                { "lm-g710n", "G7ThinQ" },
                { "ILCE-6000", "A6000" },
                { "ILCE-7SM2", "SonyA7S2" }
            };

        public string BuildFileName(MediaFileInfo file, int? sequence = null)
        {
            var dt = file.CapturedAt!.Value;

            string modelPart = "";
            var rawModel = file.CameraModel ?? "";

            // ⭐ 스크립트 로직 복구: 제조사 이름(Samsung, Apple 등)을 대소문자 구분 없이 제거
            var cleanedModel = Regex.Replace(rawModel, "samsung|apple", "", RegexOptions.IgnoreCase);

            // 모델명이 비어있거나, Unknown 단어가 포함된 경우 무시하고 생략
            if (!string.IsNullOrWhiteSpace(cleanedModel) &&
                !cleanedModel.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                !cleanedModel.Equals("UnknownCamera", StringComparison.OrdinalIgnoreCase))
            {
                // 불순물이 제거된 모델명(SM-S908N)으로 딕셔너리에서 검색
                var finalModel = ModelAliasMap.TryGetValue(cleanedModel, out var alias) ? alias : cleanedModel;
                modelPart = $"_{finalModel}";
            }

            // 1. 이름 조립 (기기명이 없으면 _ 생략됨)
            var name = $"{dt:yyyyMMdd_HHmmss}{modelPart}";
            if (sequence.HasValue) name += $"_{sequence.Value:D3}";

            // 2. 확장자 처리 (무조건 소문자)
            var ext = (file.Extension ?? "").ToLowerInvariant();

            // 3. jpeg -> jpg 변환
            if (ext == "jpeg")
            {
                ext = "jpg";
            }

            return $"{name}.{ext}";
        }

        public string GetArchiveFolder(string root, DateTime dt)
            => Path.Combine(root, $"{dt.Year}년", $"{dt.Month:D2}월");
    }
}