using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhiStore.Addons.PhiSave2
{
    /// <summary>
    /// 获取 TapTap APK 信息与下载直链
    /// </summary>
    public class TapTapApkLinkCore
    {
        private static readonly Random Rnd = new Random();

        private const string NonceChars = "abcdefghijklmnopqrstuvwxyz0123456789";

        public int DefaultAppId { get; set; } = 165287;

        public async Task<ApkInfo> GetApkInfoAsync(int appId)
        {
            var resolvedAppId = appId > 0 ? appId : DefaultAppId;
            // Delegate to PhiInfoService which is already non-Godot
            var svc = new PhiInfo.PhiInfoService();
            var info = await svc.GetApkInfoAsync(resolvedAppId);
            return new ApkInfo
            {
                DownloadUrl = info.DownloadUrl,
                FileName = info.FileName,
                VersionCode = info.VersionCode,
                VersionName = info.VersionName
            };
        }

        public Task<ApkInfo> GetDownloadUrlAsync(int appId = 0)
        {
            return GetApkInfoAsync(appId);
        }

        public sealed class ApkInfo
        {
            public string? DownloadUrl { get; set; }
            public string? FileName { get; set; }
            public int VersionCode { get; set; }
            public string? VersionName { get; set; }
        }

        public static int? ExtractVersionCodeFromFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var match = Regex.Match(fileName, "-(\\d+)\\.apk$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var code))
            {
                return code;
            }

            return null;
        }

        public static string? FindLocalApkWithHighestVersion(string? cloudFileName)
        {
            if (string.IsNullOrWhiteSpace(cloudFileName))
            {
                return null;
            }

            var match = Regex.Match(cloudFileName, "^(.+?)-(\\d+)\\.apk$");
            if (!match.Success)
            {
                return null;
            }

            var prefix = match.Groups[1].Value;
            var pattern = $"{prefix}-*.apk";
            var files = Directory.GetFiles(".", pattern);
            if (files.Length == 0)
            {
                return null;
            }

            int maxCode = -1;
            string? maxFile = null;
            foreach (var file in files)
            {
                var nameOnly = Path.GetFileName(file);
                var code = ExtractVersionCodeFromFileName(nameOnly);
                if (code.HasValue && code.Value > maxCode)
                {
                    maxCode = code.Value;
                    maxFile = file;
                }
            }

            return maxFile;
        }
    }
}
