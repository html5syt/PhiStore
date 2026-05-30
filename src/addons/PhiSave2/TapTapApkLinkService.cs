using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace PhiStore.Addons.PhiSave2;

[GlobalClass]
public partial class TapTapApkLinkRequest : RefCounted
{
    [Signal]
    public delegate void CompletedEventHandler(string downloadUrl, string fileName, int versionCode, string versionName);

    [Signal]
    public delegate void ErrorEventHandler(string message);

    public void Resolve(string downloadUrl, string fileName, int versionCode, string versionName)
    {
        CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.Completed, downloadUrl, fileName, versionCode, versionName);
    }

    public void Reject(string message)
    {
        CallDeferred(GodotObject.MethodName.EmitSignal, SignalName.Error, message);
    }
}

[GlobalClass]
public partial class TapTapApkLinkService : RefCounted
{
    public int DefaultAppId { get; set; } = 165287;

    public TapTapApkLinkRequest GetDownloadUrlAsync(int appId = 0)
    {
        var request = new TapTapApkLinkRequest();
        var core = new TapTapApkLinkCore { DefaultAppId = this.DefaultAppId };
        var resolvedAppId = appId > 0 ? appId : core.DefaultAppId;

        Task.Run(async () =>
        {
            try
            {
                var info = await core.GetDownloadUrlAsync(resolvedAppId);
                request.Resolve(info.DownloadUrl ?? string.Empty, info.FileName ?? string.Empty, info.VersionCode, info.VersionName ?? string.Empty);
            }
            catch (Exception ex)
            {
                request.Reject(ex.Message);
            }
        });

        return request;
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

        var match = Regex.Match(fileName, @"-(\d+)\.apk$", RegexOptions.IgnoreCase);
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

        var match = Regex.Match(cloudFileName, @"^(.+?)-(\d+)\.apk$");
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