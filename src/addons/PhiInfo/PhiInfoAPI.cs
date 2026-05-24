using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using PhiInfo.Processing;
using Shua.UA.Core.Asset;
using Shua.Zip;
using Shua.Zip.ReadAt;
using PhiInfo.Processing.DataProvider;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    // --- 定义&初始化部分 ---
    private PhiInfoContext? _context;
    private Language _currentLanguage = Language.zh_cn;

    /// <summary>
    /// 手动释放当前持有的上下文资源并断开流。
    /// 在不再需要读取资源时，可由 GDScript 主动调用以快速回收内存并关闭文件/网络句柄。
    /// </summary>
    private readonly PhiInfoService _service = new PhiInfoService();
    public void FreeContext()
    {
        _context?.Dispose();
        _context = null;
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
    /// 资源类型枚举（GDScript 与 C# 均可使用，写法参考 `APILanguage`）。
    /// - `Illustration`: 曲绘（主要高清图）
    /// - `Background`: 曲绘模糊/背景图
    /// - `Music`: 音频文件
    /// - `Chart`: 谱面 JSON
    /// - `CollectionAsset`: 收藏品或自定义资源路径
    /// - `Avatar`: 头像资源
    /// </summary>
    // Shared ResourceType is declared in PhiInfoService.cs.

    // --- 加载apk部分 ---

    /// <summary>
    /// 统一的 Provider 构建辅助函数，用于合并多处重复的文件挂载与实例化逻辑。
    /// </summary>
    // Provider/context initialization is handled by PhiInfoService (non-Godot). API should remain a thin Godot wrapper.

    /// <summary>
    /// 从单个本地 APK 文件及 classdata (cldb) 文件初始化实例。
    /// </summary>
    public void InitFromSingleApk(string apkPath, string cldbPath) => InitFromLocalApk(new[] { apkPath }, cldbPath);

    /// <summary>
    /// 从多个分卷 APK 包及 classdata 文件初始化实例。
    /// </summary>
    public void InitFromLocalApk(string[] apkPaths, string cldbPath)
    {
        _currentLanguage = Language.zh_cn;
        var globalApks = apkPaths.Select(p => ProjectSettings.GlobalizePath(p)).ToArray();
        var globalCldb = ProjectSettings.GlobalizePath(cldbPath);
        _service.InitFromLocalApk(globalApks, globalCldb);
        _context = _service.Context;
    }

    /// <summary>
    /// 从单个 Web 网络直链及 classdata (cldb) 文件初始化实例。
    /// </summary>
    public void InitFromSingleWebApk(string apkUrl, string cldbPath) => InitFromWebApk(new[] { apkUrl }, cldbPath);

    /// <summary>
    /// 从多个 Web 网络直链分卷 APK 及本地 classdata 文件初始化实例，使用 HTTP 的 Range 流式解包。
    /// </summary>
    public void InitFromWebApk(string[] apkUrls, string cldbPath)
    {
        _currentLanguage = Language.zh_cn;
        var globalCldb = ProjectSettings.GlobalizePath(cldbPath);
        _service.InitFromWebApk(apkUrls, globalCldb);
        _context = _service.Context;
    }

    /// <summary>
    /// 使用独立线程非阻塞初始化。
    /// </summary>
    public void InitFromSingleApkAsync(string apkPath, string cldbPath) => InitFromSingleApkAsync(apkPath, cldbPath, (int)APILanguage.Chinese);
    public void InitFromSingleApkAsync(string apkPath, string cldbPath, int apiLanguage)
    {
        _currentLanguage = ConvertApiLanguage(apiLanguage);
        _service.InitFromLocalApkAsync(
            ProjectSettings.GlobalizePath(apkPath),
            ProjectSettings.GlobalizePath(cldbPath),
            (s, p) => CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, s, p),
            (success, error) => CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, success, error)
        );
    }

    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath) => InitFromSingleWebApkAsync(apkUrl, cldbPath, (int)APILanguage.Chinese);
    public void InitFromSingleWebApkAsync(string apkUrl, string cldbPath, int apiLanguage)
    {
        _currentLanguage = ConvertApiLanguage(apiLanguage);
        _service.InitFromWebApkAsync(
            apkUrl,
            ProjectSettings.GlobalizePath(cldbPath),
            (s, p) => CallDeferred(MethodName.EmitSignal, SignalName.InitializationProgress, s, p),
            (success, error) => CallDeferred(MethodName.EmitSignal, SignalName.InitializationCompleted, success, error)
        );
    }

    // Initialization flow uses PhiInfoService's async helpers; keep API simple and use service for engine-agnostic work.

    /// <summary>
    /// 获取或设置底层的 PhiInfoContext 上下文实例。
    /// 可以直接传入已有上下文实现快速初始化。
    /// </summary>
    public PhiInfoContext Context
    {
        get => _context ?? throw new InvalidOperationException("PhiInfoAPI context is not initialized.");
        set => _context = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// 获取或设置当前的语言选项。
    /// </summary>
    public Language CurrentLanguage
    {
        get => _currentLanguage;
        set => _currentLanguage = value;
    }

    private static Language ConvertApiLanguage(int apiLanguage) => apiLanguage switch
    {
        (int)APILanguage.Chinese => Language.zh_cn,
        (int)APILanguage.TraditionalChinese => Language.zh_tw,
        (int)APILanguage.English => Language.en,
        (int)APILanguage.Japanese => Language.ja,
        (int)APILanguage.Korean => Language.ko,
        _ => Language.zh_cn
    };

    private PhiInfoContext EnsureContext()
        => _context ?? throw new InvalidOperationException("PhiInfoAPI context is not initialized.");

    // --- 获取metadata部分 ---

    /// <summary>
    /// 获取所有支持的语言列表。
    /// </summary>
    /// <returns>所有的 Language 枚举列表</returns>
    public List<Language> GetSupportedLanguages()
    {
        return _service.GetSupportedLanguages().ToList();
    }

    /// <summary>
    /// 获取歌曲元数据信息（C# 原生类型）。
    /// </summary>
    public List<SongInfo> GetSongsData() => _service.GetSongsData();

    /// <summary>
    /// 获取歌曲元数据信息（Godot Variant）。
    /// </summary>
    public Godot.Variant GetSongs() => Godot.Variant.CreateFrom(ConvertSongInfoList(GetSongsData()));

    /// <summary>
    /// 获取收集品元数据信息（C# 原生类型）。
    /// </summary>
    public List<Folder> GetCollectionData() => _service.GetCollectionData();

    /// <summary>
    /// 获取收集品元数据信息（Godot Variant）。
    /// </summary>
    public Godot.Variant GetCollection() => Godot.Variant.CreateFrom(ConvertFolderList(GetCollectionData()));

    /// <summary>
    /// 获取头像元数据信息（C# 原生类型）。
    /// </summary>
    public List<Avatar> GetAvatarsData() => _service.GetAvatarsData();

    /// <summary>
    /// 获取头像元数据信息（Godot Variant）。
    /// </summary>
    public Godot.Variant GetAvatars() => Godot.Variant.CreateFrom(ConvertAvatarList(GetAvatarsData()));

    /// <summary>
    /// 获取章节元数据信息（C# 原生类型）。
    /// </summary>
    public List<ChapterInfo> GetChaptersData() => _service.GetChaptersData();

    /// <summary>
    /// 获取章节元数据信息（Godot Variant）。
    /// </summary>
    public Godot.Variant GetChapters() => Godot.Variant.CreateFrom(ConvertChapterInfoList(GetChaptersData()));

    /// <summary>
    /// 获取资源目录数据（C# 原生类型）。
    /// </summary>
    public Dictionary<string, string> GetAssetCatalogData() => _service.GetAssetCatalogData();

    /// <summary>
    /// 获取 Tips 信息（包含所有语言的原生字典数据）。
    /// </summary>
    public Dictionary<Language, List<string>> GetTipsData() => _service.GetTipsData();

    public string[] GetTips() => SelectTips(GetTipsData(), CurrentLanguage);

    /// <summary>
    /// 获取所有元数据信息（C# 原生类型）。
    /// </summary>
    public AllInfo GetAllInfoData() => _service.GetAllInfoData();

    /// <summary>
    /// 获取所有元数据信息（Godot Variant）。
    /// </summary>
    public Godot.Variant GetAllInfo() => Godot.Variant.CreateFrom(ConvertAllInfo(GetAllInfoData()));

    public AsyncAssetRequest GetSongsAsync() => RunAsync(GetSongs);
    public AsyncAssetRequest GetCollectionAsync() => RunAsync(GetCollection);
    public AsyncAssetRequest GetAvatarsAsync() => RunAsync(GetAvatars);
    public AsyncAssetRequest GetChaptersAsync() => RunAsync(GetChapters);
    public AsyncAssetRequest GetTipsAsync() => RunAsync(() => Godot.Variant.CreateFrom(GetTips()));
    public AsyncAssetRequest GetAllInfoAsync() => RunAsync(GetAllInfo);

    /// <summary>
    /// 获取版本信息。
    /// </summary>
    public PhiVersion GetPhiVersionData() => _service.GetPhiVersionData();

    /// <summary>
    /// 获取版本信息（Godot Variant）。
    /// </summary>
    public Godot.Collections.Dictionary GetPhiVersion() => ConvertPhiVersion(GetPhiVersionData());

    public AsyncAssetRequest GetPhiVersionAsync() => RunAsync(() => Godot.Variant.CreateFrom(GetPhiVersion()));

    /// <summary>
    /// 获取资源目录及其路径的字典映射（Godot Variant）。
    /// </summary>
    public Godot.Collections.Dictionary GetAssetCatalog() => ConvertAssetCatalog(GetAssetCatalogData());

    public AsyncAssetRequest GetAssetCatalogAsync() => RunAsync(() => Godot.Variant.CreateFrom(GetAssetCatalog()));

    // --- 获取资源部分 --- 

    /// <summary>
    /// 统一资源请求入口。
    /// </summary>
    public Godot.Variant GetResource(string sid, ResourceType type, int diff)
    {
        var path = _service.BuildResourcePath(sid, type, diff);
        return GetAsset(path);
    }

    /// <summary>
    /// 统一资源请求入口：不指定难度时使用默认值。
    /// </summary>
    public Godot.Variant GetResource(string sid, ResourceType type) => GetResource(sid, type, -1);

    /// <summary>
    /// 统一资源请求入口：接受整型类型值（例如通过 `PhiInfoAPI.ResourceType` 常量集合传入）。
    /// </summary>
    public Godot.Variant GetResource(string sid, int type, int diff) => GetResource(sid, (ResourceType)type, diff);
    public Godot.Variant GetResource(string sid, int type) => GetResource(sid, (ResourceType)type, -1);

    /// <summary>
    /// 统一资源请求入口：异步版本。
    /// </summary>
    public AsyncAssetRequest GetResourceAsync(string sid, ResourceType type, int diff)
        => GetAssetAsync(_service.BuildResourcePath(sid, type, diff));

    /// <summary>
    /// 统一资源请求入口：异步版本，不指定 diff 时使用默认值。
    /// </summary>
    public AsyncAssetRequest GetResourceAsync(string sid, ResourceType type) => GetResourceAsync(sid, type, -1);

    /// <summary>
    /// 统一资源请求入口：异步版本，接受整型类型值（例如通过 `PhiInfoAPI.ResourceType` 常量集合传入）。
    /// </summary>
    public AsyncAssetRequest GetResourceAsync(string sid, int type, int diff) => GetResourceAsync(sid, (ResourceType)type, diff);
    public AsyncAssetRequest GetResourceAsync(string sid, int type) => GetResourceAsync(sid, type, -1);

    // Path building and raw asset extraction moved to PhiInfoService (engine-agnostic).

    /// <summary>
    /// 统合解析资源文件。根据路径后缀自动推断类型。
    /// </summary>
    /// <param name="assetPath">资源标识或路径</param>
    public Godot.Variant GetAsset(string assetPath)
    {
        var raw = _service.GetRawAsset(assetPath);
        switch (raw.Type)
        {
            case PhiInfoService.RawAssetResult.Kind.JsonString:
                return Godot.Variant.CreateFrom(raw.Json ?? string.Empty);
            case PhiInfoService.RawAssetResult.Kind.MusicBytes:
                var music = raw.Music ?? Array.Empty<byte>();
                if (music.Length == 0) throw new InvalidOperationException($"无法解码音频: {assetPath}");
                return Godot.Variant.CreateFrom(AudioStreamOggVorbis.LoadFromBuffer(music));
            case PhiInfoService.RawAssetResult.Kind.Image:
                {
                    if (raw.Image == null) throw new InvalidOperationException($"无法解码图片: {assetPath}");
                    using var normalized = raw.Image.CloneAs<Rgba32>();
                    var data = new byte[normalized.Width * normalized.Height * 4];
                    normalized.CopyPixelDataTo(data);
                    return Godot.Variant.CreateFrom(Godot.Image.CreateFromData(
                        normalized.Width,
                        normalized.Height,
                        false,
                        Godot.Image.Format.Rgba8,
                        data));
                }
            default:
                throw new NotSupportedException($"Unsupported asset type: {assetPath}");
        }
    }

    public AsyncAssetRequest GetAssetAsync(string assetPath) => RunAsync(() => GetAsset(assetPath));

    // --- 其他工具函数 ---

    /// <summary>
    /// 选择当前语言对应的 Tips 列表，找不到时回退到简体中文或第一组数据。
    /// </summary>
    private string[] SelectTips(Dictionary<Language, List<string>> tips, Language language)
    {
        if (tips.TryGetValue(language, out var selected) && selected.Count > 0)
            return selected.ToArray();

        if (tips.TryGetValue(Language.zh_cn, out var fallback) && fallback.Count > 0)
            return fallback.ToArray();

        var first = tips.Values.FirstOrDefault(list => list.Count > 0);
        return first?.ToArray() ?? [];
    }

    private static Godot.Collections.Dictionary ConvertPhiVersion(PhiVersion version)
    {
        return new Godot.Collections.Dictionary
        {
            { "code", (long)version.code },
            { "name", version.name }
        };
    }

    private static Godot.Collections.Array ConvertSongInfoList(IEnumerable<SongInfo> songs)
        => ConvertArray(songs, song => Godot.Variant.CreateFrom(ConvertSongInfo(song)));

    private static Godot.Collections.Dictionary ConvertSongInfo(SongInfo song)
    {
        return new Godot.Collections.Dictionary
        {
            { "id", song.id },
            { "key", song.key },
            { "name", song.name },
            { "composer", song.composer },
            { "illustrator", song.illustrator },
            { "preview_time", song.preview_time },
            { "preview_end_time", song.preview_end_time },
            { "levels", Godot.Variant.CreateFrom(ConvertSongLevelDictionary(song.levels)) }
        };
    }

    private static Godot.Collections.Dictionary ConvertSongLevelDictionary(Dictionary<string, SongLevel> levels)
        => ConvertDictionary(
            levels,
            levelKey => Godot.Variant.CreateFrom(levelKey),
            level => Godot.Variant.CreateFrom(ConvertSongLevel(level)));

    private static Godot.Collections.Dictionary ConvertSongLevel(SongLevel level)
    {
        return new Godot.Collections.Dictionary
        {
            { "charter", level.charter },
            { "difficulty", level.difficulty }
        };
    }

    private static Godot.Collections.Array ConvertFolderList(IEnumerable<Folder> folders)
        => ConvertArray(folders, folder => Godot.Variant.CreateFrom(ConvertFolder(folder)));

    private static Godot.Collections.Dictionary ConvertFolder(Folder folder)
    {
        return new Godot.Collections.Dictionary
        {
            { "title", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(folder.title)) },
            { "sub_title", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(folder.sub_title)) },
            { "cover", folder.cover },
            { "files", Godot.Variant.CreateFrom(ConvertFileItemList(folder.files)) }
        };
    }

    private static Godot.Collections.Array ConvertFileItemList(IEnumerable<FileItem> files)
        => ConvertArray(files, file => Godot.Variant.CreateFrom(ConvertFileItem(file)));

    private static Godot.Collections.Dictionary ConvertFileItem(FileItem fileItem)
    {
        return new Godot.Collections.Dictionary
        {
            { "key", fileItem.key },
            { "sub_index", fileItem.sub_index },
            { "name", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(fileItem.name)) },
            { "date", fileItem.date },
            { "supervisor", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(fileItem.supervisor)) },
            { "category", fileItem.category },
            { "content", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(fileItem.content)) },
            { "properties", Godot.Variant.CreateFrom(ConvertLanguageStringDictionary(fileItem.properties)) }
        };
    }

    private static Godot.Collections.Array ConvertAvatarList(IEnumerable<Avatar> avatars)
        => ConvertArray(avatars, avatar => Godot.Variant.CreateFrom(ConvertAvatar(avatar)));

    private static Godot.Collections.Dictionary ConvertAvatar(Avatar avatar)
    {
        return new Godot.Collections.Dictionary
        {
            { "name", avatar.name },
            { "addressable_key", avatar.addressable_key }
        };
    }

    private static Godot.Collections.Array ConvertChapterInfoList(IEnumerable<ChapterInfo> chapters)
        => ConvertArray(chapters, chapter => Godot.Variant.CreateFrom(ConvertChapterInfo(chapter)));

    private static Godot.Collections.Dictionary ConvertChapterInfo(ChapterInfo chapter)
    {
        return new Godot.Collections.Dictionary
        {
            { "code", chapter.code },
            { "banner", chapter.banner },
            { "song_ids", Godot.Variant.CreateFrom(ConvertStringArray(chapter.song_ids)) }
        };
    }

    private static Godot.Collections.Dictionary ConvertAllInfo(AllInfo allInfo)
    {
        return new Godot.Collections.Dictionary
        {
            { "version", Godot.Variant.CreateFrom(ConvertPhiVersion(allInfo.version)) },
            { "songs", Godot.Variant.CreateFrom(ConvertSongInfoList(allInfo.songs)) },
            { "collection", Godot.Variant.CreateFrom(ConvertFolderList(allInfo.collection)) },
            { "avatars", Godot.Variant.CreateFrom(ConvertAvatarList(allInfo.avatars)) },
            { "tips", Godot.Variant.CreateFrom(ConvertLanguageTipDictionary(allInfo.tips)) },
            { "chapters", Godot.Variant.CreateFrom(ConvertChapterInfoList(allInfo.chapters)) }
        };
    }

    private static Godot.Collections.Dictionary ConvertLanguageTipDictionary(Dictionary<Language, List<string>> tips)
        => ConvertDictionary(
            tips,
            language => Godot.Variant.CreateFrom(language.ToString()),
            values => Godot.Variant.CreateFrom(ConvertStringArray(values)));

    private static Godot.Collections.Dictionary ConvertLanguageStringDictionary(Dictionary<Language, string> values)
        => ConvertDictionary(
            values,
            language => Godot.Variant.CreateFrom(language.ToString()),
            value => Godot.Variant.CreateFrom(value));

    private static Godot.Collections.Dictionary ConvertAssetCatalog(Dictionary<string, string> catalog)
        => ConvertDictionary(
            catalog,
            key => Godot.Variant.CreateFrom(key),
            value => Godot.Variant.CreateFrom(value));

    private static Godot.Collections.Array ConvertStringArray(IEnumerable<string> values)
        => ConvertArray(values, value => Godot.Variant.CreateFrom(value));

    private static Godot.Collections.Array ConvertArray<T>(IEnumerable<T> values, Func<T, Godot.Variant> converter)
    {
        var array = new Godot.Collections.Array();
        foreach (var value in values)
            array.Add(converter(value));
        return array;
    }

    private static Godot.Collections.Dictionary ConvertDictionary<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> values,
        Func<TKey, Godot.Variant> keyConverter,
        Func<TValue, Godot.Variant> valueConverter)
        where TKey : notnull
    {
        var dictionary = new Godot.Collections.Dictionary();
        foreach (var (key, value) in values)
            dictionary[keyConverter(key)] = valueConverter(value);
        return dictionary;
    }

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
}
