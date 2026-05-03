## Plan: PhiSave2 C# 接口接入计划

这是为在 `src/addons/PhiSave2` 目录下使用 C# 编写 `PhiSave2` 接口（接入 `PhigrosLibraryCSharp`）制定的实现计划。

**Steps**
1. **基础模块搭建与类结构设计 (Phase 1)**
   - 在 `src/addons/PhiSave2` 创建 `PhiSaveAPI.cs`。
   - 使用 `[GlobalClass]` 和继承 `Godot.RefCounted` 将 C# 类暴露给 GDScript。
   - 设计并实现异步机制，封装 C# 的 `Task<T>` 以非阻塞方式执行操作，并使用 Godot `[Signal]` 向 GDScript 抛出进度、成功及异常事件。
   - 编写详尽的 XML 注释以提供类参考文档。
2. **认证模块实现 (Phase 2)**
   - 编写 `StartTapTapLoginAsync` 方法。
   - 内部调用 `PhigrosLibraryCSharp.CloudSave.Login.TapTapLogin`。
   - 发送包含二维码 URL 或 Token 的信号供 UI 展示，等待扫码并返回 `sessionToken`。
3. **云端下载、解密与本地保存 (Phase 3)**
   - 编写 `DownloadSaveAsync(string sessionToken)` 方法，使用 `GetSaveInfoFromCloudAsync()` 和 `GetSaveZipAsync()`。
   - 编写 `LoadZipAsync(byte[] zipData)` 使用 `SaveContext.FromZipAsync` 解析 ZIP 并解密为明文内部数据。
   - 提供方法将 `byte[]` 写入本地 Godot `FileAccess`。
4. **全量明文 JSON 导出/导入 (Phase 4)**
   - 编写 `ExportDataAsJson()`: 将 `SaveContext` 内的游戏记录序列化为完整 JSON 字符串。
   - 编写 `ImportDataFromJson(string json)`: 反序列化 JSON 字符串并覆盖内部对应的记录状态。
5. **定数数据结构与 RKS 计算 (Phase 5)**
   (*平行于 Phase 4*)
   - 定义类似 `PhiInfoAPI.cs` 中枚举和字典机制。
   - 统一 GDScript 传入的定数数据结构（如 JSON 或 Godot Dictionary），C# 端解析为 `Dictionary<ChartConstantKey, float>` 结构。
   - 编写 `CalculateRKS(Godot.Collections.Dictionary constantsMap)` 方法，调用 `context.ReadGameRecord().GetSortedListForRks`，返回 RKS 数值。
6. **存档重打包与云端上传 (Phase 6)**
   - 编写 `GenerateSummaryBinary()` 用于根据计算的 RKS 和现有游戏记录生成 summary 二进制信息。
   - 编写 `PackSaveToZipAsync()` 将修改后的明文数据加密并压缩成 ZIP 格式 `byte[]`。
   - 参考 `SaveUploader.cs` 移植上传功能，编写 `UploadSaveAsync`。由于 V4 版本 `Save.Client` 已公开，将直接利用 `HttpClient` 进行 ZIP 分块并上传 TapTap API。

**Relevant files**
- `src/addons/PhiSave2/PhiSaveAPI.cs` — 实现 Godot 与核心功能交互的主入口和类结构，提供信号和方法封装；定义定数传输的数据结构内部类。
- `src/addons/PhiSave2/PhiSaveUploader.cs` — 将上传逻辑独立（或作为内部类组织），专门封装对 TapTap 相关端口的分段和直接通讯（基于提取的 `SaveUploader.cs` 代码和公共 `HttpClient`）。

**Verification**
1. 编译 C# 解决方案 (`dotnet build`) 没有错误或警告。
2. 在 Godot 中编写简单的 GDScript 测试脚本 (`addons/PhiSave2/Test/`) 设置 `PhiSaveAPI` 并在 GDScript 成功接收 `LoginQRCode` 信号。
3. 验证 JSON 导出后结构是否完整可读。
4. 提供定数 Mock Dictionary，执行 RKS 计算并打印结果确认计算准确。

**Decisions**
- 返回值由一开始设想的 JSON 字符串改为使用 `Godot.Collections.Dictionary` 及其相关集合类型，确保 GDScript 拿到后可以直接安全调用和遍历。对于性能热点若发生卡顿，可以在未来评估。
- 二维码使用纯字符串抛出，依赖项目已有的二维码组建在 GDScript 层渲染。
