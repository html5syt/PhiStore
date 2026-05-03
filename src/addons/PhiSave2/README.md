PhiSave2 — Godot C# 插件使用说明

概述
- PhiSave2 是在 `src/addons/PhiSave2` 下实现的 Godot C# 插件，封装了对 Phigros 游戏存档的读取/解密/修改/重打包以及 TapTap/LeanCloud 云端上传下载流程。
- 对外主入口类为 `PhiSave2`（同时保留兼容壳 `PhiSaveAPI`）。
- 依赖：`PhigrosLibraryCSharp`（通过 NuGet 引入），Godot 4 C# 环境。

主要功能
- TapTap 二维码登录 / 回调登录（非阻塞, 返回 AsyncSaveRequest）
- 从云端加载存档并缓存为内存条目（支持读取 summary）
- 读取/修改单个解密后的存档条目（byte[]）
- 导出/导入明文 JSON（供 GDScript 层展示或编辑）
- 基于定数表计算 RKS（返回 Godot.Dictionary 可直接在 GDScript 使用）
- 生成/打包/加密本地 ZIP，上传至云端

文件与类参考（快捷导航）
- `src/addons/PhiSave2/PhiSave2.cs` — 主入口类，保存会话/缓存，提供基础读写、打包、保存接口。
- `src/addons/PhiSave2/AsyncSaveRequest.cs` — 异步请求封装，带进度/完成信号，供 GDScript 非阻塞调用。
- `src/addons/PhiSave2/PhiSave2.AuthAndCloud.cs` — 认证、云端加载与上传相关方法（StartQrLoginAsync、BeginCallbackLogin、CompleteCallbackLoginAsync、LoadCloudSaveAsync、UploadToCloudAsync）。
- `src/addons/PhiSave2/PhiSave2.DataAndRks.cs` — 明文导出/导入、可读数据导出、RKS 计算方法（ExportPlainData、ImportPlainData、CalculateRks 等）。
- `src/addons/PhiSave2/PhiSave2.Internal.cs` — 私有工具方法：上下文缓存、ByteReader 解析、加解密、打包、ToGodotVariant 等。
- `src/addons/PhiSave2/PhiSaveUploader.cs` — 云端上传实现（分片上传、file token、回调等），已移植并适配 HttpClient。

快速示例（GDScript）
- 创建并初始化：
```
    var api = PhiSave2.new()
    api.InitSession(session_token)
```
- 二维码登录（异步）：
```
    var req = api.StartQrLoginAsync(true, ["public_profile"]) # 返回 AsyncSaveRequest
    req.connect("completed", Callable(self, "_on_login_completed"))
    req.connect("progress", Callable(self, "_on_login_progress"))
```
- 加载云端第 0 个存档并导出可读数据：
```
    var loadReq = api.LoadCloudSaveAsync(0)
    loadReq.connect("completed", Callable(self, "_on_load_completed"))

    func _on_load_completed(result, err):
        if err != "":
            print("加载失败: ", err)
            return
        var readable = api.ExportReadableData({}) # 传入定数 map 或空
        print(readable)
```
- 导出明文（Base64 entries）：
```
    var plain = api.ExportPlainData()
    # 在 GDScript 中 plain 是 Dictionary，可直接写入 user:// 文件
```
- 计算 RKS：
```
    var constants = {"song_id": [1.0, 5.0, 11.2, 15.0]} # 示例结构
    var rks = api.CalculateRks(constants)
    print(rks)
```
注意事项与建议
- `PhiSave2` 的返回值多为 `Godot.Collections.Dictionary/Array`（通过内部 JSON 序列化转换），GDScript 端可直接使用。
- 项目开启 `PublishAOT` 时，某些 `System.Text.Json` 或 `HttpClient.Json` 的便捷方法可能触发 AOT 警告；当前代码使用了显式序列化/解析并对部分调用做了抑制注释。
- 对于大型上传/下载操作，使用 `AsyncSaveRequest` 的进度信号避免阻塞主线程。
- 如需在 GDScript 中直接渲染二维码，请监听 `LoginQrCodeReadyEventHandler` 信号并使用返回的 url。

开发/调试
- 本地编译：在 `src` 下运行 `dotnet build`（确保 NuGet 包已还原）。
- 运行时：在 Godot 编辑器中将类作为 Autoload 或直接在场景中实例化。
- 如果需要扩展：优先在 partial 文件中添加方法（`PhiSave2.*.cs` 分文件结构），保持对外接口稳定。

联系方式与贡献
- 该实现基于 `PhigrosLibraryCSharp` 的 API 与 `PhigrosSaveDumper` 中的上传流程实现，贡献请发起 PR 并在 PR 描述中说明测试步骤。
