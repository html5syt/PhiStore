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

## PhiSaveTools

1. 存档工具类，提供存档相关的工具函数

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

# ConfigFile

## 描述

1. 写入于`config.cfg`中，用于保存`sessiontoken`等信息
2. 读取时，优先读取`config.cfg`，若不存在则创建并写入需要的内容。
3. 在`PhiSave`中使用`Config`变量进行读取和写入操作。

## 结构

1. `Config`
   1. `sessionToken`：保存登录后获取的`sessiontoken`
   2. `nickName`：保存登录后的昵称
   3. `shortId`：保存登录后的短id **（sessiontoken登录=`session`）**
   4. `uuid`：保存TDS登录后的uuid用于强制上传存档
2. `SaveInfo`
   1. `updateTime`：保存上次更新云存档的时间戳

# 数据结构

## gameKey

### type

| No. | value           | type               | 示例（keyname）                   | 对应flag | 说明    |
| --- | --------------- | ------------------ | ----------------------------- | ------ | ----- |
| 1   | [0, 0, 0, 0, 1] | 头像                 | Introduction                  | 1      |       |
| 2   | [0, 1, 0, 0, 0] | 付费解锁歌曲             | Believe Light (feat. 果丸哒呦)    | 1      |       |
| 3   | [0, 0, 0, 1, 0] | 曲绘                 | life flashes before weeb eyes | 1      |       |
| 4   | [1, 0, 1, 0, 0] | 收集品（剧情）            | shijian13                     | 3      |       |
| 5   | [0, 1, 0, 1, 0] | 单曲精选集歌曲            | 万吨匿名信                         | 2      | 疑似等效2 |
| 6   | [0, 0, 0, 1, 1] | 附带头像的歌曲（头像名与歌曲名重合） | Dlyrotz, Glaciaxion           | 4      |       |
| 7   | [0, 1, 0, 1, 1] | 附带头像的歌曲（头像名与歌曲名重合） | 云女孩, dB doll                  | 4      |       |

### flag

| No. | value     | type | 示例（keyname） | 说明                                                 |
| --- | --------- | ---- | ----------- | -------------------------------------------------- |
| 1   | [1]       |      | 光           |                                                    |
| 2   | [1, 1]    |      | 今天不是明天      |                                                    |
| 3   | [1~x,1~x] |      | trophon     | 剧情专用，指示收集百分比，`collection.tsv`第三列为总分块数，value两个元素相等。 |
| 4   | [1, 1, 1] |      | 云女孩         | 1+2得到                                              |


# RankingScore 计算

# 注意事项

1. 若使用`sessiontoken`需要保存到`ConfigFile`中，以便下次登录时自动填充
2. 始终下载/上传最新更新的存档
3. ~~暂时无法完成存档初始化（云端需要有存档）~~提供uuid可初始化存档

# 备注

1. md坑死我了`slice`居然前闭后开,还有saveVersion为啥在PCA里被强制改成81，导致我一直报存档版本太高
2. 生成summary处计数逻辑存在问题，会导致缺少数量
