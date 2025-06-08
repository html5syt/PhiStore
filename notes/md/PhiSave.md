# 框架

## PhiSave

1. 对接存档打包+云存档
2. 登录+回调二次处理
3. 本地存档数据处理(初始化)
4. 提供函数用于读取云存档数据

## CloudSave

1. 云存档下载/上传
2. 云存档数据转本地存档（反序列化到JSON，备选xml导出？）

## PackSave

1. 云存档打包/解包
2. 云存档加密/解密（序列化/反序列化）

## DataType

1. 存档数据类型定义

# 流程

1. 发起登录请求
2. 判断当前环境：
   1. 安卓
      1. 调用GodotTap完成登录
      2. 调用GodotTap获取存档summary信息
      3. 下载到`user://.save`
   2. 其他
      1. 输入`sessiontoken`
      2. 调用`CloudSave`下载云存档
3. 调用`PackSave`解包云存档到`user://PhigrosSave.json`
4. **上传时**：
   1. 调用`PackSave`打包本地存档到`user://.save`
   2. 调用`CloudSave`或者`GodotTap`上传云存档到云端
   3. **上传通用流程**：
      1. 获取当前存档id并记录
      2. 上传新存档到云端
      3. 使用旧id删除老的云端存档（**成功后删除本地存档**）

# 注意事项

1. 若使用`sessiontoken`需要保存到`ConfigFile`中，以便下次登录时自动填充
2. 始终下载/上传最新更新的存档
3. 暂时无法完成存档初始化（云端需要有存档）

# 备注

1. md坑死我了
`slice`居然前闭后开
还有saveVersion为啥在PCA里被强制改成81，导致我一直报存档版本太高



# 完善错误处理