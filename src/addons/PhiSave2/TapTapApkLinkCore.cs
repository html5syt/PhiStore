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
    /// 非 Godot 的核心实现：用于获取 TapTap APK 信息与下载直链
    /// </summary>
    public class TapTapApkLinkCore
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly Random Rnd = new Random();

        private const string XUATemplate = "V=1&PN=TapTap&VN=2.40.1-rel.100000&VN_CODE=240011000&LOC=CN&LANG=zh_CN&CH=default&UID={0}&NT=1&SR=1080x2030&DEB=Xiaomi&DEM=Redmi+Note+5&OSV=9";
        private const string SignSalt = "PeCkE6Fu0B10Vm9BKfPfANwCUAn5POcs";
        private const string NonceChars = "abcdefghijklmnopqrstuvwxyz0123456789";

        public int DefaultAppId { get; set; } = 165287;

        private static string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            var builder = new StringBuilder();
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        private static string CreateNonce()
        {
            var chars = new char[5];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = NonceChars[Rnd.Next(NonceChars.Length)];
            }
            return new string(chars);
        }

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
