## Plan: PhiSave2 C# 接口设计与实现 (详细版)

为 Godot 项目提供一个基于 PhigrosLibraryCSharp 的高性能、异步、类型安全的存档管理接口。

**TL;DR**
在 `src/addons/PhiSave2` 下实现 `PhiSave2API`。核心流程包括：
1. **登录**：TapTap 设备码获取 -> 二维码生成 -> MAC 签名轮询状态 -> LeanCloud 身份换取。
2. **存档**：Section 级别的 AES 解密 -> C# 对象化 -> 本地加密持久化。
3. **上传**：Summary 二进制编码 -> 七牛云 Multipart 上传 -> LeanCloud 元数据修正。

**Steps**

### 第一阶段：基础架构与数据模型 (Infrastructure & Models)
1. **存档对象化 (PhiSaveData)**：
    - 定义 `PhiSaveData` 包装类，内部持有 `GameRecord`, `GameProgress`, `GameUserInfo`, `GameSettings` 实例。
    - 实现 `IPartSerializer`，利用库中的 `ByteReader` 和 `ByteWriter` 完成二进制 Section 与 JSON 的互转。
2. **本地保护实现**：
    - `AESUtil`：实现 AES-256-CBC 模式。
    - 持久化流：`Data` -> `JSON` -> `GZip` -> `AES` -> `user://phisave2/save.dat`。

### 第二阶段：登录流程细节 (Authentication Flow)
1. **TapTap QR 完整时序**：
    - **请求**：POST `device/code` (携带 `client_id` 和 `device_id`)。
    - **显示**：解析返回的 `qrcode_url` 并通过信号通知 Godot 挂载至 Texture。
    - **轮询 (Polling)**：每 3-5 秒请求 `oauth2/v1/token`，直至返回 200。
    - **签名换取 (MAC Auth)**：使用返回的 `mac_key` 对 LeanCloud `/1.1/users` 接口进行 HmacSha1 签名。构造如下 `authData`：
      ```json
      { "authData": { "taptap": { "kid": "...", "access_token": "...", "mac_key": "..." } } }
      ```
2. **Session 管理**：支持 `sessionToken` 的自动刷新校验与安全存储。

### 第三阶段：同步与云端流水线 (Sync & Upload Pipeline)
1. **云端下载与解包**：
    - 调用 `Save.GetSaveZipAsync`。
    - 遍历 Zip 中的 Entry（如 `gameRecord`），调用 `Save.Decrypt` 后由 `FromReader` 填充到 `PhiSaveData`。
2. **上传流水线 (SaveUploader)**：
    - **Step 1: 生成 Summary**：构建二进制包，注意偏移量：
      - `0x04`: Float32 (**RKS**) | `0x0A`: Avatar 字符串长度 | `0x11+n`: 12组 UInt16 (**Rating**)
    - **Step 2: 七牛云 Multipart**：
      - 获取 `fileTokens` -> 初始化 `uploadId` -> `PUT` 存档 Zip 分片 -> `CompleteUpload`。
      - 调用 `fileCallback` 通知服务端文件生效。
    - **Step 3: 元数据更新**：更新 LeanCloud `_GameSave` 表，同步 Base64 后的 `summary` 字符串。

### 第四阶段：数值计算引擎 (Calculation)
1. **RKS 核心逻辑**：
    - **单曲计算**：$rks = (\frac{acc - 55}{45})^2 \times Difficulty$ (仅当 $acc > 70$)。
    - **整合策略 (B27 + Top3Phi)**：
      - 排序所有成绩 RKS，取前 27 名。
      - 筛选出已 Phi 的成绩，取其 RKS 前 3 名。
      - 总 RKS = (Sum(Best27) + Sum(Top3Phi)) / 30。

### 第五阶段：集成与异步 (Integration)
1. **Godot 对接**：
    - 使用 `Godot.RefCounted` 作为基类。
    - 封装 `AsyncAssetRequest` 用于管理独立的线程任务。

**Relevant files**
- `src/addons/PhiSave2/PhiSave2API.cs` — 主入口类
- `src/addons/PhiSave2/PhiSaveUploader.cs` — 云端上传逻辑实现（含 Summary 编码）
- `src/addons/PhiSave2/Internal/RKSCalculator.cs` — RKS 算法实现
- `src/addons/PhiSave2/Models/PhiSaveData.cs` — 存档实体类

**Verification**
1. **登录验证**：确认扫码后能成功进入游戏主界面并拉取到用户名。
2. **计算验证**：手动修改某首曲目分数，重新计算 RKS，对比官方计算工具。
3. **完整同步**：执行“修改本地 -> 计算 -> 上传 -> 删除本地 -> 重新下载”，验证数据是否回滚。

**Decisions**
- **计算标准**：严格对齐 Phigros 1.6.x+ 的 **B27+3** 算法。
- **存储**：本地存档增加 GZip 压缩层以优化 Godot `user://` 空间的读写性能。