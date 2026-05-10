# PhiSave2 测试场景使用指南

本测试场景 (`test_phisave2.tscn` + `test_phisave2.gd`) 展示了 PhiSave2 的完整功能，包括登录、存档管理、冲突解决和定数计算。

## 功能概览

### 1. 登录方式（三选一）

#### QR Code 登录
- **使用方式**: 点击 "QR Code Login" 按钮
- **流程**:
  1. 系统生成二维码 URL
  2. 使用 `addons/qr_code` 中的 `QRCodeRect` 实时渲染二维码
  3. 用户扫描二维码并授权
  4. 系统自动轮询确认登录状态
  5. 成功后收到 `LoginSuccess` 信号，包含 sessionToken 和 userObjectId

#### OAuth 登录
- **使用方式**: 点击 "OAuth Login" 按钮
- **流程**:
  1. 启动本地 OAuth 回调监听（默认 8080 端口）
  2. 生成授权 URL 并通过 `OAuthUrlGenerated` 信号通知
  3. 点击 "Open OAuth Link" 使用系统默认浏览器打开授权页面
  4. 用户授权后，本地监听器接收回调码
  5. 系统自动交换 code 获取 sessionToken
  6. 通过 `OAuthLoginResult` 信号返回结果

#### Session Token 初始化
- **使用方式**: 粘贴已有的 sessionToken，点击 "Init" 按钮
- 用于快速测试，跳过登录流程

### 2. 存档管理

#### 下载存档 (Download Save from Cloud)
- 从云端下载已加密的存档
- 解包 ZIP 并使用 `SaveContext` 反序列化
- 将内容加载到内存（`PhiSave2API.CurrentSave`）
- 返回旧存档的文件 ID 和对象 ID（用于后续上传）

#### 检查冲突 (Check for Conflicts)
- **第一步**: 获取本地与云端存档的元数据
  - 修改时间（UTC）
  - RKS 值
- **第二步**: 若元数据不同，则进行差异分析
  - 逐歌曲逐难度比较分数和准确率
  - 生成差异列表，显示每个不同的 score/accuracy
- **第三步**: 显示冲突解决面板

#### 冲突解决（三选一）

1. **Merge Both (合并)**
   - 合并本地和云端存档
   - 每首歌取分数较高的版本
   - 更新内存中的 `CurrentSave`
   - 自动保存到本地加密文件

2. **Keep Local (Upload) (保留本地)**
   - 丢弃云端存档
   - 以内存中的本地版本为准
   - 重新包装成 ZIP 并上传到云端
   - 更新云端存档内容

3. **Keep Cloud (Download) (保留云端)**
   - 丢弃本地存档
   - 从云端重新下载完整存档
   - 覆盖内存中的 `CurrentSave`
   - 保持本地加密备份同步

### 3. 本地持久化

#### 保存到本地 (Save to Local)
- 使用 AES-256-CBC + GZip 加密
- 自动生成随机密钥和 IV（首次运行时生成）
- 保存路径: `user://phi_save_local.enc`
- 内容: PhiSaveData JSON + GZip + AES

#### 从本地加载 (Load from Local)
- 从 `user://phi_save_local.enc` 读取
- 使用同一密钥和 IV 解密
- 恢复内存中的 `CurrentSave`

### 4. 数据导入导出

#### 导出为 JSON (Export as JSON)
- 将内存中的存档序列化为 JSON
- 包含所有游戏进度、关卡数据、用户信息等
- 显示前 500 字符预览

#### 从 JSON 导入 (Import JSON)
- 接受标准 JSON 格式的存档数据
- 反序列化并加载到 `CurrentSave`

### 5. 定数计算（RKS）

#### 加载定数数据
- 从 Phigros APK 或本地资源加载 PhiInfo 数据
- 解析每首歌的难度定数信息
- 构建映射表: `{songId}_{difficultyIndex}` -> 定数值

#### 计算 RKS (Calculate RKS)
- 使用 `RKSCalculator` 进行计算
- 考虑所有游玩记录
- 返回总 RKS 值
- 自动保存到 `CurrentSave.SummaryRks`

## 信号连接对照

| C# 信号 | GDScript 处理函数 | 触发条件 |
|---------|-----------------|---------|
| `QrCodeGenerated` | `_on_qr_code_generated` | QR 登录时生成二维码 |
| `LoginSuccess` | `_on_login_success` | 登录成功 |
| `LoginFailed` | `_on_login_failed` | 登录失败 |
| `OAuthUrlGenerated` | `_on_oauth_url_generated` | OAuth 授权 URL 生成 |
| `OAuthLoginResult` | `_on_oauth_login_result` | OAuth 流程完成 |
| `AsyncPhiSaveRequest.Completed` | 回调函数 | 异步操作成功 |
| `AsyncPhiSaveRequest.Error` | 回调函数 | 异步操作失败 |

## 使用流程示例

### 场景 A: 首次登录并下载存档
1. 点击 "QR Code Login"
2. 扫描显示的二维码
3. 等待登录成功（显示 "Logged in successfully"）
4. 点击 "Download Save from Cloud"
5. 等待下载完成（日志显示 "Save downloaded successfully"）

### 场景 B: 处理存档冲突
1. 登录并下载存档（见场景 A）
2. 点击 "Check for Conflicts"
3. 若检测到冲突，冲突面板会显示差异列表
4. 选择解决方案：
   - "Merge Both": 自动合并，保留更高分数
   - "Keep Local": 用本地覆盖云端并上传
   - "Keep Cloud": 用云端覆盖本地并下载
5. 等待操作完成（日志显示相应消息）

### 场景 C: 离线使用和恢复
1. 登录并下载存档
2. 点击 "Calculate RKS" 计算当前 RKS
3. 点击 "Save to Local" 保存加密备份
4. 关闭游戏
5. 下次打开，使用已保存的 token：
   - 粘贴 token 到输入框
   - 点击 "Init"
6. 点击 "Load from Local" 恢复存档
7. 点击 "Export as JSON" 查看或备份数据

## 配置说明

### OAuth 配置
在 `test_phisave2.gd` 中修改以下变量：
```gdscript
var oauth_port: int = 8080
var oauth_auth_endpoint: String = "https://example.com/oauth/authorize"
var oauth_token_endpoint: String = "https://example.com/oauth/token"
var oauth_client_id: String = "client_id_here"
var oauth_client_secret: String = "client_secret_here"
```

### PhiInfo 数据
需要准备 Phigros APK 文件：
- 放置在 `user://phigros_installed.apk`
- 或修改 `_load_phi_info()` 中的路径

### 加密密钥
- 首次运行时自动生成，保存在变量中
- 每次运行都需要重新生成（可选择持久化密钥）

## 日志输出

所有操作都会输出详细日志，包括：
- 操作状态（成功 ✓ / 失败 ✗）
- 错误信息
- 数据摘要（如文件 ID、RKS 值等）

日志显示在底部的 TextEdit 中，支持滚动查看历史记录。

## 故障排查

### OAuth 登录失败
- 检查 `oauth_port` 是否被占用
- 确认本地 HTTP 监听器正常工作
- 验证 clientId 和 clientSecret

### 定数数据加载失败
- 检查 APK 文件是否存在且有效
- 确认 PhiInfo addon 已正确安装
- 查看日志输出以了解具体错误

### 存档下载/上传失败
- 检查网络连接
- 确认云端账户有有效的存档
- 验证 sessionToken 是否过期
- 查看详细错误信息（显示在日志中）

## 代码参考

### AsyncPhiSaveRequest 使用模式
```gdscript
var request = phi_save2_api.SomeAsyncMethod()
request.Completed.connect(func(result):
    # 处理成功情况
    print("Operation successful: ", result)
)
request.Error.connect(func(error):
    # 处理错误情况
    print("Operation failed: ", error)
)
```

### 信号连接最佳实践
- 在 `_ready()` 中统一连接所有信号
- 使用 `CallDeferred` 确保 Godot 主线程安全
- 始终提供 Error 处理回调

## 注意事项

1. **线程安全**: 所有 C# 异步操作都使用 `CallDeferred` 确保 GDScript 端线程安全
2. **内存管理**: PhiSave2API 使用 RefCounted，自动释放资源
3. **加密密钥**: 实际应用中应持久化密钥，而非每次重新生成
4. **用户隐私**: 所有敏感数据（token、密钥等）应妥善存储和传输
