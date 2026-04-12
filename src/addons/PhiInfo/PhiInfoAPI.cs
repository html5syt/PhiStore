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
    /// 获取所有的歌曲元数据信息。
    /// </summary>
    /// <returns>包含歌曲各类详细信息的集合 (若给 Godot 使用建议调用 GetSongsJson 进行解析)</returns>
    public List<SongInfo> GetSongs()
    {
        return _context.Info.ExtractSongInfo();
    }

    /// <summary>
    /// 将歌曲信息序列化为 JSON 字符串，方便 GDScript 通过 JSON.parse 直接读取由于非 Godot 对象产生的反射屏蔽。
    /// </summary>
    public string GetSongsJson()
    {
        var songs = GetSongs();
        return System.Text.Json.JsonSerializer.Serialize(songs);
    }

    /// <summary>
    /// 将合集信息序列化为 JSON 字符串。
    /// </summary>
    public string GetCollectionJson()
    {
        var data = _context.Info.ExtractCollection();
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// 将头像信息序列化为 JSON 字符串。
    /// </summary>
    public string GetAvatarsJson()
    {
        var data = _context.Info.ExtractAvatars();
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// 将主线章节信息序列化为 JSON 字符串。
    /// </summary>
    public string GetChaptersJson()
    {
        var data = _context.Info.ExtractChapters();
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// 将资源目录序列化为 JSON 字符串。
    /// </summary>
    public string GetAssetCatalogJson()
    {
        var data = GetAssetCatalog();
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    /// <summary>
    /// 获取所有的合集/按包分类的收藏夹。
    /// </summary>
    /// <returns>包含各个文件夹/章节的列表</returns>
    public List<Folder> GetCollection()
    {
        return _context.Info.ExtractCollection();
    }

    /// <summary>
    /// 获取所有的头像信息。
    /// </summary>
    /// <returns>头像信息列表</returns>
    public List<Avatar> GetAvatars()
    {
        return _context.Info.ExtractAvatars();
    }

    /// <summary>
    /// 获取所有的提示信息(Tips)字符串。
    /// </summary>
    /// <returns>提示信息的字符串列表</returns>
    public string[] GetTips()
    {
        return _context.Info.ExtractTips().ToArray();
    }

    /// <summary>
    /// 获取主线章节的详细信息。
    /// </summary>
    /// <returns>包含各主线章节数据的列表</returns>
    public List<ChapterInfo> GetChapters()
    {
        return _context.Info.ExtractChapters();
    }

    /// <summary>
    /// 获取以上所有数据的归总大类 (AllInfo)。
    /// </summary>
    /// <returns>包含了游玩所需的全部曲目、章节等等的 AllInfo 对象</returns>
    public AllInfo GetAllInfo()
    {
        return _context.Info.ExtractAllInfo();
    }

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
    /// 解析并获取指定的文本资源 (UnityText) 内容。
    /// </summary>
    /// <param name="assetName">资源名或路径</param>
    /// <returns>解析出的字符串文本</returns>
    public string GetAssetText(string assetName)
    {
        var rawPath = GetRawAssetPath(assetName);
        if (rawPath == null) throw new FileNotFoundException($"Catalog missing tracking for {assetName}");

        using var textData = _context.Bundle.Get<UnityText>(rawPath);
        return textData.Content;
    }

    /// <summary>
    /// 解析并获取指定的背景音乐或曲目 (UnityMusic)，并作为 Godot 的可播放音频流返回。
    /// </summary>
    /// <param name="assetName">资源名或路径</param>
    /// <returns>Godot 中的 AudioStreamOggVorbis 数据，可以直接挂载到 AudioStreamPlayer</returns>
    /// <exception cref="InvalidOperationException">当音乐解码失败时抛出</exception>
    public AudioStreamOggVorbis GetAssetMusic(string assetName)
    {
        var rawPath = GetRawAssetPath(assetName);
        if (rawPath == null) throw new FileNotFoundException($"Catalog missing tracking for {assetName}");

        var musicData = PhiInfoDecoders.DecoderMusic(_context.Bundle.Get<UnityMusic>(rawPath));
        if (musicData == null || musicData.Length == 0)
            throw new InvalidOperationException($"无法解码音频资源: {assetName}");

        return AudioStreamOggVorbis.LoadFromBuffer(musicData);
    }

    /// <summary>
    /// 解析并获取指定的图片资源 (UnityImage)，并作为 Godot 原生 Image 结构返回。
    /// 可以通过 ImageTexture.CreateFromImage(image) 转化为 Godot 的贴图格式用于渲染。
    /// </summary>
    /// <param name="assetName">资源名或路径</param>
    /// <returns>解析出的 Godot.Image 对象</returns>
    /// <exception cref="InvalidOperationException">当图片无法解析或读取时抛出</exception>
    public Godot.Image GetAssetImage(string assetName)
    {
        var rawPath = GetRawAssetPath(assetName);
        if (rawPath == null) throw new FileNotFoundException($"Catalog missing tracking for {assetName}");

        using var image = (SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>)PhiInfoDecoders.DecoderImage(_context.Bundle.Get<UnityImage>(rawPath));
        if (image == null)
            throw new InvalidOperationException($"无法解码图像资源: {assetName}");

        var width = image.Width;
        var height = image.Height;
        var data = new byte[width * height * 3];
        image.CopyPixelDataTo(data);

        return Godot.Image.CreateFromData(width, height, false, Godot.Image.Format.Rgb8, data);
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
