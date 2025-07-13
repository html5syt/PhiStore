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

---

---

## PhiSaveTools

1. 存档工具类，提供存档相关的工具函数

## SaveWorker

1. 读写JSON存档

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

| **index**       | 0         | 1       | 2          | 3       | 4       |                |
| --------------- | --------- | ------- | ---------- | ------- | ------- | -------------- |
|                 |           |         |            |         |         |                |
| ***flag***Array | [5，       | *null*, | 5，         | *null*, | *null*] | **例1：trophon** |
| ***type***Array | [1，       | 0，      | 1，         | 0，      | 0]      | **例1：trophon** |
|                 |           |         |            |         |         |                |
| ***flag***Array | [*null*，  | 1,      | *null*，    | 1,      | 1]      | **例2：云女孩**     |
| ***type***Array | [0，       | 1，      | 0，         | 1，      | 1]      | **例2：云女孩**     |
| **说明**          | 看了几个收藏品碎片 | 是否解锁单曲  | 收集到几个收藏品碎片 | 曲绘      | 头像      |                |

**解析方法：遍历type数组，遇到1就从flag里获取一个数覆盖上去。**

*注：将type的0/1作为false/true对待。*

# RankingScore 计算

已实现。

# 注意事项

1. 若使用`sessiontoken`需要保存到`ConfigFile`中，以便下次登录时自动填充
2. 始终下载/上传最新更新的存档
3. ~~暂时无法完成存档初始化（云端需要有存档）~~提供uuid可初始化存档

# 备注

1. md坑死我了`slice`居然前闭后开,还有saveVersion为啥在PCA里被强制改成81，导致我一直报存档版本太高
2. 生成summary处计数逻辑存在问题，会导致缺少数量
