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
using Shua.Zip;
using Shua.Zip.ReadAt;
using PhiInfo.Processing.DataProvider;

namespace PhiStore.Addons.PhiInfo;

[GlobalClass]
public partial class AsyncAssetRequest : Godot.RefCounted
{
    [Signal]
    public delegate void CompletedEventHandler(Godot.Variant result);

    public void SetResult(Godot.Variant result)
    {
        EmitSignal(SignalName.Completed, result);
    }

    public void SetError(string message)
    {
        EmitSignal(SignalName.Completed, default(Godot.Variant));
    }
}

/// <summary>
/// PhiInfo 数据的核心 Godot 对接接口包装类。
/// 提供了直接从 PhiInfoCore 的数据上下文中提取歌曲、章节等信息以及获取媒体资源的功能。
/// </summary>
[GlobalClass]
public partial class PhiInfoAPI : RefCounted
{
    private PhiInfoContext _context;

    /// <summary>
    /// 手动释放当前持有的上下文资源并断开流。
    /// 在不再需要读取资源时，可由 GDScript 主动调用以快速回收内存并关闭文件/网络句柄。
    /// </summary>
    public void FreeContext()
    {
        if (_context != null)
        {
            _context.Dispose();
            _context = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) FreeContext();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 初始化进度状态枚举
    /// </summary>
    public enum InitState : int
    {
        Starting = 0,
        ReadingFiles = 1,
        MountingProvider = 2,
        BuildingContext = 3,
        Completed = 4,
        Error = 5
    }

    [Signal]
    public delegate void InitializationProgressEventHandler(int state, float progress);

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
    /// 统一的 Provider 构建辅助函数，用于合并多处重复的文件挂载与实例化逻辑
    /// </summary>
    private AndroidPackagesDataProvider CreateProviders(string[] paths, string cldbPath, bool isWeb)
    {
        var globalCldbPath = ProjectSettings.GlobalizePath(cldbPath);
        var readAts = paths.Select(path => isWeb ? (IReadAt)new HttpReadAt(path) : new MmapReadAt(ProjectSettings.GlobalizePath(path))).ToArray();
        var zips = readAts.Select(r => new ShuaZip(r)).ToArray();
        var cldbStream = System.IO.File.OpenRead(globalCldbPath);
        return new AndroidPackagesDataProvider(zips, cldbStream);
    }

    /// <summary>
    /// 从单个本地 APK 文件及 classdata (cldb) 文件初始化实例。
    /// </summary>
    public void InitFromSingleApk(string apkPath, string cldbPath) => InitFromLocalApk(new[] { apkPath }, cldbPath);

    /// <summary>
    /// 从多个分卷 APK 包及 classdata 文件初始化实例。
    /// </summary>
    public void InitFromLocalApk(string[] apkPaths, string cldbPath) => _context = new PhiInfoContext(CreateProviders(apkPaths, cldbPath, false), Language.Chinese);

    /// <summary>
    /// 从单个 Web 网络直链及 classdata (cldb) 文件初始化实例。
    /// </summary>
    public void InitFromSingleWebApk(string apkUrl, string cldbPath) => InitFromWebApk(new[] { apkUrl }, cldbPath);

    /// <summary>
    /// 从多个 Web 网络直链分卷 APK 及本地 classdata 文件初始化实例，使用 HTTP 的 Range 流式解包。
    /// </summary>
    public void InitFromWebApk(string[] apkUrls, string cldbPath) => _context = new PhiInfoContext(CreateProviders(apkUrls, cldbPath, true), Language.Chinese);

    /// <summary>
    /// 使用独立线程非阻塞初始化。
    /// </summary>
    public void InitFromSingleApkAsync(string apkPath, string cldbPath) => InitFromSingleApkAsync(apkPath, cldbPath, (int)APILanguage.Chinese);
    public void InitFromSingleApkAsync(string apkPath, string cldbPath, int apiLanguage) =>
        InitAsync(() => CreateProviders(new[] { apkPath }, cldbPath, false), (Language)apiLanguage);

    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath) => InitFromSingleWebApkAsync(apkUrl, cldbPath, (int)APILanguage.Chinese);
    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath, int apiLanguage) =>
        InitAsync(() => CreateProviders(new[] { apkUrl }, cldbPath, true), (Language)apiLanguage);

    private void InitAsync(Func<AndroidPackagesDataProvider> providerFactory, Language language)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            FreeContext(); // 在新初始化前清理旧上下文及关联的文件流/Http连接
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (i == 0) CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, (int)InitState.Starting, 0.1f);
                    var dp = providerFactory();
                    if (i == 0) CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, (int)InitState.MountingProvider, 0.6f);
                    _context = new PhiInfoContext(dp, language);
                    CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, (int)InitState.Completed, 1.0f);
                    CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, true, string.Empty);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, (int)InitState.Error, 1.0f);
                        CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, false, ex.Message);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(500);
                    }
                }
            }
        });
    }

    /// <summary>
    /// 获取或设置底层的 PhiInfoContext 上下文实例。
    /// 可以直接传入已有上下文实现快速初始化。
    /// </summary>
    public PhiInfoContext Context
    {
        get => _context;
        set => _context = value;
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
    /// 获取歌曲元数据信息。
    /// </summary>
    public Godot.Variant GetSongs() => ToGodotVariant(_context.Info.ExtractSongInfo());

    /// <summary>
    /// 获取合集元数据信息。
    /// </summary>
    public Godot.Variant GetCollection() => ToGodotVariant(_context.Info.ExtractCollection());

    /// <summary>
    /// 获取头像元数据信息。
    /// </summary>
    public Godot.Variant GetAvatars() => ToGodotVariant(_context.Info.ExtractAvatars());

    /// <summary>
    /// 获取章节元数据信息。
    /// </summary>
    public Godot.Variant GetChapters() => ToGodotVariant(_context.Info.ExtractChapters());

    /// <summary>
    /// 获取资源目录数据。
    /// </summary>
    public Godot.Variant GetAssetCatalogData() => ToGodotVariant(GetAssetCatalog());

    /// <summary>
    /// 获取 Tips 信息。
    /// </summary>
    public string[] GetTips() => _context.Info.ExtractTips().ToArray();

    /// <summary>
    /// 获取所有元数据归总信息。
    /// </summary>
    public Godot.Variant GetAllInfo() => ToGodotVariant(_context.Info.ExtractAllInfo());

    public AsyncAssetRequest GetSongsAsync() => RunAsync(GetSongs);
    public AsyncAssetRequest GetCollectionAsync() => RunAsync(GetCollection);
    public AsyncAssetRequest GetAvatarsAsync() => RunAsync(GetAvatars);
    public AsyncAssetRequest GetChaptersAsync() => RunAsync(GetChapters);
    public AsyncAssetRequest GetAssetCatalogDataAsync() => RunAsync(GetAssetCatalogData);
    public AsyncAssetRequest GetTipsAsync() => RunAsync(() => (Godot.Variant)GetTips());
    public AsyncAssetRequest GetAllInfoAsync() => RunAsync(GetAllInfo);

    /// <summary>
    /// 获取版本信息。
    /// </summary>
    public Godot.Collections.Dictionary GetPhiVersion()
    {
        var ver = _context.Info.GetPhiVersion();
        return new Godot.Collections.Dictionary { { "code", ver.code }, { "name", ver.name } };
    }

    public AsyncAssetRequest GetPhiVersionAsync() => RunAsync(() => (Godot.Variant)GetPhiVersion());

    /// <summary>
    /// 获取资源目录及其路径的字典映射。
    /// </summary>
    public Dictionary<string, string> GetAssetCatalog() => _context.Catalog.GetAll()
        .Where(v => v.Key.IsString && v.Value != null && v.Value.Value.IsString)
        .ToDictionary(v => v.Key.Str!, v => v.Value!.Value.Str);

    public AsyncAssetRequest GetAssetCatalogAsync() => RunAsync(() => ToGodotVariant(GetAssetCatalog()));

    private AsyncAssetRequest RunAsync(Func<Godot.Variant> action)
    {
        var request = new AsyncAssetRequest();
        System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    request.CallDeferred(AsyncAssetRequest.MethodName.SetResult, action());
                    return;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        request.CallDeferred(AsyncAssetRequest.MethodName.SetError, ex.Message);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(500); // 失败时增加重试机制，缓解网络主机强制关闭链接的情况
                    }
                }
            }
        });
        return request;
    }

    /// <summary>
    /// 提供曲目名字（ID），获取曲绘 (Illustration)。
    /// </summary>
    public Godot.Variant GetIllustration(string sid) => GetAsset($"Assets/Tracks/{sid}/Illustration.jpg");

    /// <summary>
    /// 提供曲目名字（ID），获取背景图 (Background)。
    /// </summary>
    public Godot.Variant GetBackground(string sid) => GetAsset($"Assets/Tracks/{sid}/IllustrationBlur.jpg");

    /// <summary>
    /// 提供曲目名字（ID），获取音频 (Music)。
    /// </summary>
    public Godot.Variant GetMusic(string sid) => GetAsset($"Assets/Tracks/{sid}/music.wav");

    /// <summary>
    /// 提供曲目名字（ID），获取指定难度的谱面 (Chart)。
    /// </summary>
    public Godot.Variant GetChart(string sid, int diff) => GetAsset($"Assets/Tracks/{sid}/Chart_{GetDiffStr(diff)}.json");

    /// <summary>
    /// 获取收藏品资源。
    /// </summary>
    public Godot.Variant GetCollectionAsset(string name) => GetAsset(name);

    /// <summary>
    /// 获取头像资源。
    /// </summary>
    public Godot.Variant GetAvatar(string name) => GetAsset($"avatar.{name}");

    public AsyncAssetRequest GetIllustrationAsync(string sid) => GetAssetAsync($"Assets/Tracks/{sid}/Illustration.jpg");
    public AsyncAssetRequest GetBackgroundAsync(string sid) => GetAssetAsync($"Assets/Tracks/{sid}/IllustrationBlur.jpg");
    public AsyncAssetRequest GetMusicAsync(string sid) => GetAssetAsync($"Assets/Tracks/{sid}/music.wav");
    public AsyncAssetRequest GetChartAsync(string sid, int diff) => GetAssetAsync($"Assets/Tracks/{sid}/Chart_{GetDiffStr(diff)}.json");
    public AsyncAssetRequest GetCollectionAssetAsync(string name) => GetAssetAsync(name);
    public AsyncAssetRequest GetAvatarAsync(string name) => GetAssetAsync($"avatar.{name}");

    private string GetDiffStr(int diff) => diff switch { 0 => "EZ", 1 => "HD", 2 => "IN", 3 => "AT", _ => "IN" };

    /// <summary>
    /// 统合解析资源文件。根据路径后缀自动推断类型。
    /// </summary>
    /// <param name="assetPath">资源标识或路径</param>
    public Godot.Variant GetAsset(string assetPath)
    {
        var cat = GetAssetCatalog();
        if (!cat.TryGetValue(assetPath, out string rawPath))
        {
            rawPath = cat.FirstOrDefault(kvp => kvp.Key.Equals(assetPath, StringComparison.OrdinalIgnoreCase)).Value
                ?? throw new FileNotFoundException($"Catalog missing tracking for {assetPath}");
        }

        if (assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var data = _context.Bundle.Get<UnityText>(rawPath);
            return Godot.Variant.CreateFrom(data.Content);
        }

        if (assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var music = PhiInfoDecoders.DecoderMusic(_context.Bundle.Get<UnityMusic>(rawPath));
            if (music == null || music.Length == 0) throw new InvalidOperationException($"无法解码音频: {assetPath}");
            return Godot.Variant.CreateFrom(AudioStreamOggVorbis.LoadFromBuffer(music));
        }

        if (assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith("avatar.", StringComparison.OrdinalIgnoreCase) ||
            assetPath.Contains("Illustration", StringComparison.OrdinalIgnoreCase))
        {
            using var image = (SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>)PhiInfoDecoders.DecoderImage(_context.Bundle.Get<UnityImage>(rawPath));
            if (image == null) throw new InvalidOperationException($"无法解码图像: {assetPath}");

            var data = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(data);
            return Godot.Variant.CreateFrom(Godot.Image.CreateFromData(image.Width, image.Height, false, Godot.Image.Format.Rgb8, data));
        }
        throw new NotSupportedException($"Unsupported asset type: {assetPath}");
    }

    public AsyncAssetRequest GetAssetAsync(string assetPath) => RunAsync(() => GetAsset(assetPath));
}
