using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PhiInfo.Core;
using PhiInfo.Core.Asset;
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using SixLabors.ImageSharp;
using GlobalIReadAt = global::Shua.Zip.IReadAt;
using GlobalMmapReadAt = global::Shua.Zip.ReadAt.MmapReadAt;
using GlobalHttpReadAt = global::Shua.Zip.ReadAt.HttpReadAt;
using GlobalShuaZip = global::Shua.Zip.ShuaZip;
using GlobalAndroidPackagesDataProvider = global::PhiInfo.Processing.DataProvider.AndroidPackagesDataProvider;

namespace PhiStore.Addons.PhiInfo;

/// <summary>
/// PhiInfo 数据的核心 Godot 对接接口包装类。
/// 提供了直接从 PhiInfoCore 的数据上下文中提取歌曲、章节等信息以及获取媒体资源的功能。
/// </summary>
[GlobalClass]
public partial class PhiInfoAPI : RefCounted
{
    private PhiInfoContext _context;

    [Signal]
    public delegate void InitializationProgressEventHandler(string status, float progress);

    [Signal]
    public delegate void InitializationCompletedEventHandler(bool success, string errorMsg);

    /// <summary>
    /// Godot 环境中专用的支持语言枚举，以便与库内部进行安全映射
    /// </summary>
    public enum APILanguage : int
    {
        Chinese = 0x28,
        TraditionalChinese = 0x29,
        English = 0x0A,
        Japanese = 0x16,
        Korean = 0x17
    }

    /// <summary>
    /// GDScript 跨语言调用所需的无参构造函数
    /// </summary>
    public PhiInfoAPI() { }

    /// <summary>
    /// 初始化 PhiInfoAPIGodotWrapper
    /// </summary>
    /// <param name="context">底层传入的 PhiInfoContext 上下文实例。请确保在外部进行数据提供者（DataProvider）的挂载初始化。</param>
    public PhiInfoAPI(PhiInfoContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// 从单个本地 APK 文件及 classdata (cldb) 文件初始化实例。
    /// 可以被 GDScript 直接调用，如: api.InitFromSingleApk("user://...", "res://...")
    /// </summary>
    /// <param name="apkPath">APK 文件路径</param>
    /// <param name="cldbPath">classdata.tpk 文件的路径</param>
    public void InitFromSingleApk(string apkPath, string cldbPath)
    {
        InitFromLocalApk(new[] { apkPath }, cldbPath);
    }

    /// <summary>
    /// 从多个分卷 APK 包及 classdata 文件初始化实例。
    /// </summary>
    /// <param name="apkPaths">包含主副包在内的多个 APK 路径集合（支持 Godot 协议前缀）</param>
    /// <param name="cldbPath">classdata.tpk 文件的路径</param>
    public void InitFromLocalApk(string[] apkPaths, string cldbPath)
    {
        var globalCldbPath = ProjectSettings.GlobalizePath(cldbPath);
        var globalApks = apkPaths.Select(ProjectSettings.GlobalizePath).ToArray();

        var readAts = globalApks.Select(path => (GlobalIReadAt)new GlobalMmapReadAt(path)).ToArray();
        var zips = readAts.Select(r => new GlobalShuaZip(r)).ToArray();
        var cldbStream = System.IO.File.OpenRead(globalCldbPath);

        var dp = new GlobalAndroidPackagesDataProvider(zips, cldbStream);
        _context = new PhiInfoContext(dp, Language.Chinese);
    }

    /// <summary>
    /// 从单个 Web 网络直链及 classdata (cldb) 文件初始化实例。通过 HTTP 的 Range 请求进行流式解包提取资源。
    /// </summary>
    /// <param name="apkUrl">APK安装包存放的URL直链，服务器须支持带有 Range 请求头的 HTTP 206 状态返回。</param>
    /// <param name="cldbPath">本地 classdata.tpk 文件的路径</param>
    public void InitFromSingleWebApk(string apkUrl, string cldbPath)
    {
        InitFromWebApk(new[] { apkUrl }, cldbPath);
    }

    /// <summary>
    /// 从多个 Web 网络直链分卷 APK 及本地 classdata 文件初始化实例，使用 HTTP 的 Range 流式解包。
    /// </summary>
    /// <param name="apkUrls">包含主副包在内的多个 APK Web 直链集合</param>
    /// <param name="cldbPath">本地 classdata.tpk 文件的路径</param>
    public void InitFromWebApk(string[] apkUrls, string cldbPath)
    {
        var globalCldbPath = ProjectSettings.GlobalizePath(cldbPath);
        var readAts = apkUrls.Select(url => (GlobalIReadAt)new GlobalHttpReadAt(url)).ToArray();
        var zips = readAts.Select(r => new GlobalShuaZip(r)).ToArray();
        var cldbStream = System.IO.File.OpenRead(globalCldbPath);

        var dp = new GlobalAndroidPackagesDataProvider(zips, cldbStream);
        _context = new PhiInfoContext(dp, Language.Chinese);
    }

    /// <summary>
    /// 使用独立线程非阻塞初始化单个本地 APK 文件。完成后将抛出 InitializationCompleted 信号。
    /// </summary>
    public void InitFromSingleApkAsync(string apkPath, string cldbPath)
    {
        InitFromSingleApkAsync(apkPath, cldbPath, (int)APILanguage.Chinese);
    }

    public void InitFromSingleApkAsync(string apkPath, string cldbPath, int apiLanguage)
    {
        Language language = (Language)apiLanguage;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "转换本地路径...", 0.1f);
                var globalCldbPath = ProjectSettings.GlobalizePath(cldbPath);
                var globalApkPath = ProjectSettings.GlobalizePath(apkPath);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "映射本地文件内存...", 0.3f);
                var readAt = new GlobalMmapReadAt(globalApkPath);
                var zip = new GlobalShuaZip(readAt);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "读取 CLDB 文件...", 0.5f);
                var cldbStream = System.IO.File.OpenRead(globalCldbPath);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "挂载安卓资源包提供者...", 0.6f);
                var dp = new GlobalAndroidPackagesDataProvider(new[] { zip }, cldbStream);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "构建资源上下文环境(较耗时)...", 0.7f);
                _context = new PhiInfoContext(dp, language);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "初始化完成！", 1.0f);
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, true, string.Empty);
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, false, ex.Message);
            }
        });
    }

    /// <summary>
    /// 使用独立线程非阻塞初始化单个网络 Web 链接 APK 文件。完成后将抛出 InitializationCompleted 信号。
    /// </summary>
    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath)
    {
        InitFromSingleWebApkAsync(apkUrl, cldbPath, (int)APILanguage.Chinese);
    }

    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath, int apiLanguage)
    {
        Language language = (Language)apiLanguage;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "正在解析网络连接与文件头...", 0.1f);
                var globalCldbPath = ProjectSettings.GlobalizePath(cldbPath);

                var readAt = new GlobalHttpReadAt(apkUrl);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "正在读取 ZIP 树...", 0.3f);
                var zip = new GlobalShuaZip(readAt);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "读取本地 CLDB 文件...", 0.5f);
                var cldbStream = System.IO.File.OpenRead(globalCldbPath);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "挂载网络流式解包提供者...", 0.6f);
                var dp = new GlobalAndroidPackagesDataProvider(new[] { zip }, cldbStream);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "构建元数据流上下文...", 0.7f);
                _context = new PhiInfoContext(dp, language);

                CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, "网络流式解析完成！", 1.0f);
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, true, string.Empty);
            }
            catch (Exception ex)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, false, ex.Message);
            }
        });
    }

    /// <summary>
    /// 获取或设置当前的语言选项。
    /// </summary>
    public Language CurrentLanguage
    {
        get => _context.Language;
        set => _context.Language = value;
    }

    /// <summary>
    /// 获取所有支持的语言列表。
    /// </summary>
    /// <returns>所有的 Language 枚举列表</returns>
    public List<Language> GetSupportedLanguages()
    {
        return Enum.GetValues<Language>().ToList();
    }

    /// <summary>
    /// 将 C# 对象序列化并解析为 Godot 原生的 Variant (包含 Dictionary/Array) 返回
    /// </summary>
    private Godot.Variant ToGodotVariant(object obj)
    {
        return Godot.Json.ParseString(System.Text.Json.JsonSerializer.Serialize(obj));
    }

    /// <summary>
    /// 获取所有的歌曲元数据信息。
    /// </summary>
    public Godot.Variant GetSongs() => ToGodotVariant(_context.Info.ExtractSongInfo());

    /// <summary>
    /// 获取合集信息。
    /// </summary>
    public Godot.Variant GetCollection() => ToGodotVariant(_context.Info.ExtractCollection());

    /// <summary>
    /// 获取头像信息。
    /// </summary>
    public Godot.Variant GetAvatars() => ToGodotVariant(_context.Info.ExtractAvatars());

    /// <summary>
    /// 获取主线章节信息。
    /// </summary>
    public Godot.Variant GetChapters() => ToGodotVariant(_context.Info.ExtractChapters());

    /// <summary>
    /// 获取资源目录。
    /// </summary>
    public Godot.Variant GetAssetCatalogData() => ToGodotVariant(GetAssetCatalog());

    /// <summary>
    /// 获取所有的提示信息(Tips)字符串。
    /// </summary>
    public string[] GetTips() => _context.Info.ExtractTips().ToArray();

    /// <summary>
    /// 获取以上所有数据的归总大类 (AllInfo)。
    /// </summary>
    public Godot.Variant GetAllInfo() => ToGodotVariant(_context.Info.ExtractAllInfo());

    /// <summary>
    /// 获取资源版本信息 (PhiVersion)。
    /// </summary>
    /// <returns>包含 name 和 code 的字典</returns>
    public Godot.Collections.Dictionary GetPhiVersion()
    {
        var ver = _context.Info.GetPhiVersion();
        return new Godot.Collections.Dictionary
        {
            { "code", ver.code },
            { "name", ver.name }
        };
    }

    /// <summary>
    /// 获取资源目录及其路径的字典映射。
    /// 键为资源标识，值为其底层路径或别名。
    /// </summary>
    /// <returns>对应的资源目录</returns>
    public Dictionary<string, string> GetAssetCatalog()
    {
        return _context.Catalog.GetAll()
            .Where(v => v.Key.IsString && v.Value != null && v.Value.Value.IsString)
            .ToDictionary(
                v => v.Key.Str!,
                v => v.Value!.Value.Str
            );
    }

    /// <summary>
    /// 提供曲目名字（ID），获取曲绘 (Illustration)
    /// </summary>
    public Godot.Variant GetSongIllustration(string songId)
    {
        return GetAsset($"Assets/Tracks/{songId}/Illustration.jpg");
    }

    /// <summary>
    /// 提供曲目名字（ID），获取背景图 (Background)
    /// </summary>
    public Godot.Variant GetSongBackground(string songId)
    {
        return GetAsset($"Assets/Tracks/{songId}/IllustrationBlur.jpg");
    }

    /// <summary>
    /// 提供曲目名字（ID），获取音频 (Music)
    /// </summary>
    public Godot.Variant GetSongMusic(string songId)
    {
        return GetAsset($"Assets/Tracks/{songId}/music.wav"); // 也可能是 ogg
    }

    /// <summary>
    /// 提供曲目名字（ID），获取各个难度的谱面 (Chart JSON)。
    /// difficulty: 0 -> EZ, 1 -> HD, 2 -> IN, 3 -> AT
    /// </summary>
    public Godot.Variant GetSongChart(string songId, int difficulty)
    {
        string diffStr = difficulty switch
        {
            0 => "EZ",
            1 => "HD",
            2 => "IN",
            3 => "AT",
            _ => "IN" // 默认退回到 IN
        };
        return GetAsset($"Assets/Tracks/{songId}/Chart_{diffStr}.json");
    }

    /// <summary>
    /// 提供收藏品名字，获取指定收藏品纹理/图像
    /// </summary>
    public Godot.Variant GetCollectionAsset(string collectionName)
    {
        // 可以根据 collectionName 组织拼合
        return GetAsset(collectionName);
    }

    /// <summary>
    /// 提供头像名字，获取指定头像图片内容
    /// </summary>
    public Godot.Variant GetAvatarAsset(string avatarName)
    {
        return GetAsset($"avatar.{avatarName}");
    }

    /// <summary>
    /// 参照 PhiInfo CLI 逻辑，统合所有资源类型的提取。
    /// 根据路径后缀/名称自动推断应该解析为文本、音频还是图像。
    /// </summary>
    /// <param name="assetPath">资源名或底层路径 (例如 .json, .wav, .jpg, avatar.)</param>
    /// <returns>返回 Godot 中的 String, AudioStreamOggVorbis 或 Godot.Image</returns>
    public Godot.Variant GetAsset(string assetPath)
    {
        var rawPath = GetRawAssetPath(assetPath);
        if (rawPath == null) throw new FileNotFoundException($"Catalog missing tracking for {assetPath}");

        if (assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var textData = _context.Bundle.Get<UnityText>(rawPath);
            return Godot.Variant.CreateFrom(textData.Content);
        }

        if (assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var musicData = PhiInfoDecoders.DecoderMusic(_context.Bundle.Get<UnityMusic>(rawPath));
            if (musicData == null || musicData.Length == 0)
                throw new InvalidOperationException($"无法解码音频资源: {assetPath}");
            return Godot.Variant.CreateFrom(AudioStreamOggVorbis.LoadFromBuffer(musicData));
        }

        if (assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith("avatar.", StringComparison.OrdinalIgnoreCase) ||
            assetPath.Contains("Illustration", StringComparison.OrdinalIgnoreCase))
        {
            using var image = (SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>)PhiInfoDecoders.DecoderImage(_context.Bundle.Get<UnityImage>(rawPath));
            if (image == null)
                throw new InvalidOperationException($"无法解码图像资源: {assetPath}");

            var width = image.Width;
            var height = image.Height;
            var data = new byte[width * height * 3]; // Rgb24 每像素3字节
            image.CopyPixelDataTo(data);

            var godotImage = Godot.Image.CreateFromData(width, height, false, Godot.Image.Format.Rgb8, data);
            return Godot.Variant.CreateFrom(godotImage);
        }

        throw new NotSupportedException($"不支持的资源类型或后缀名: {assetPath}");
    }

    private string GetRawAssetPath(string assetName)
    {
        var cat = GetAssetCatalog();
        if (cat.TryGetValue(assetName, out string val))
            return val;

        // 兼容传入小写或各种直接路径
        var lookup = cat.FirstOrDefault(kvp => kvp.Key.Equals(assetName, StringComparison.OrdinalIgnoreCase));
        return lookup.Value ?? assetName; // 回退原样
    }
}
