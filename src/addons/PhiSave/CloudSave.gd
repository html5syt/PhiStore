# CloudSave.gd
extends RefCounted
class_name CloudSave

const BASE_URL = "https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/"
const DEFAULT_HEADERS = {
    "X-LC-Id": "rAK3FfdieFob2Nn8Am",
    "X-LC-Key": "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0",
    "User-Agent": "LeanCloud-CSharp-SDK/1.0.3",
    "Accept": "application/json"
}

var session_token: String
var headers: Dictionary
var http_request: HTTPRequest
var parent_node: Node # 添加父节点引用

signal get_save_success(save_data: PackedByteArray)
signal upload_save_success()
signal get_nickname_success(nickname: String)
signal get_summary_success(summary: Dictionary)
signal refresh_session_token_success(session_token: String)
signal upload_nickname_success()
signal upload_summary_success()


func _init(session_token: String, parent: Node):
    self.session_token = session_token
    self.headers = DEFAULT_HEADERS.duplicate()
    self.headers["X-LC-Session"] = session_token
    self.parent_node = parent
    
    # 创建HTTP请求对象并添加到场景树
    http_request = HTTPRequest.new()
    http_request.timeout = 15
    http_request.use_threads = true
    parent.add_child(http_request) # 添加到父节点

# 发送HTTP请求
func _request(method: HTTPClient.Method, url: String, custom_headers: Dictionary = {}, data = null) -> Array:
    # 合并请求头
    var final_headers = headers.duplicate()
    for key in custom_headers:
        final_headers[key] = custom_headers[key]
    
    # 添加Content-Type
    if data != null and typeof(data) == TYPE_STRING and !final_headers.has("Content-Type"):
        final_headers["Content-Type"] = "application/json"
    
    # 打印调试信息
    print("——请求信息——")
    print("请求类型: %s" % method)
    print("请求URL: %s" % url)
    print("请求头: ", final_headers)
    if data == null:
        print("请求数据: *无请求数据*")
    elif typeof(data) == TYPE_STRING:
        print("请求数据: %s" % data)
    else:
        print("请求数据: *%d bytes*" % data.size())
    
    var headers_array := []
    for item in final_headers:
        headers_array.append(item + ": " + final_headers[item])
    
    # 发起请求
    var error: int
    if data != null:
        if typeof(data) != TYPE_STRING:
            error = http_request.request_raw(url, headers_array, method, data)
        else:
            error = http_request.request(url, headers_array, method, data)
    else:
        error = http_request.request(url, headers_array, method)
    
    if error != OK:
        push_error("HTTP请求失败: %d" % error)
        return [error, null, null]
    
    # 等待响应
    var response = await http_request.request_completed
    var result: int = response[0]
    var response_code: int = response[1]
    var response_headers: PackedStringArray = response[2]
    var body: PackedByteArray = response[3]
    
    # 处理响应
    print("——响应信息——")
    print("状态码: %d" % response_code)
    if body.size() == 0:
        print("返回数据: *无返回数据*")
    else:
        var body_text = body.get_string_from_utf8()
        if body_text != "":
            print("返回数据: %s" % body_text)
        else:
            print("返回数据: *%d bytes*" % body.size())
    
    if response_code >= 400:
        push_error("HTTP错误: %d" % response_code)
        return [result, response_code, null]
    
    return [result, response_code, body]

# 获取玩家昵称
func get_nickname() -> String:
    print("调用函数: get_nickname()")
    
    var url = BASE_URL + "users/me"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取昵称失败")
        return ""
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("nickname"):
        push_error("无效的响应格式")
        return ""
    
    print('函数 "get_nickname()" 返回: %s' % response["nickname"])
    self.emit_signal("get_nickname_success", response["nickname"])
    return response["nickname"]

# 编码summary数据
static func encode_summary(summary: Dictionary) -> String:
    # 创建二进制缓冲区
    var buffer = StreamPeerBuffer.new()
    buffer.big_endian = false # 使用小端字节序
    
    # 写入saveVersion (1字节)
    buffer.put_u8(summary["saveVersion"])
    
    # 写入challenge (2字节)
    buffer.put_u16(summary["challenge"])
    
    # 写入rks (4字节浮点数)
    buffer.put_float(summary["rks"])
    
    # 写入gameVersion (1字节)
    buffer.put_u8(summary["gameVersion"])
    
    # 写入填充字节 (1字节)
    buffer.put_u8(0)
    
    # 写入avatar字符串
    var avatar_bytes = summary["avatar"].to_utf8_buffer()
    # 写入字符串长度 (1字节)
    buffer.put_u8(avatar_bytes.size())
    # 写入字符串内容
    buffer.put_data(avatar_bytes)
    
    # 写入评级数据 (12个u16值)
    for difficulty in ["EZ", "HD", "IN", "AT"]:
        for rating in summary[difficulty]:
            buffer.put_u16(rating)
    
    # 获取完整的二进制数据
    var data = buffer.get_data_array()
    
    # 转换为base64
    return Marshalls.raw_to_base64(data)

# 解码summary数据
static func decode_summary(summary_base64: String) -> Dictionary:
    # 从base64解码
    var data = Marshalls.base64_to_raw(summary_base64)
    
    # 创建读取缓冲区
    var buffer = StreamPeerBuffer.new()
    buffer.big_endian = false # 使用小端字节序
    buffer.set_data_array(data)
    
    # 读取各字段
    var save_version = buffer.get_u8()
    var challenge = buffer.get_u16()
    var rks = buffer.get_float()
    var game_version = buffer.get_u8()
    
    # 跳过填充字节
    buffer.get_u8()
    
    # 读取avatar字符串
    var avatar_length = buffer.get_u8()
    var avatar_bytes = buffer.get_data(avatar_length)
    var avatar = avatar_bytes[1].get_string_from_utf8()
    
    # 读取评级数据 (12个u16值)
    var ratings = []
    for i in range(12):
        ratings.append(buffer.get_u16())
    
    # 构建summary字典
    return {
        "saveVersion": save_version,
        "challenge": challenge,
        "rks": rks,
        "gameVersion": game_version,
        "avatar": avatar,
        "EZ": ratings.slice(0, 3),
        "HD": ratings.slice(3, 6),
        "IN": ratings.slice(6, 9),
        "AT": ratings.slice(9, 12)
    }

# 修改get_summary函数使用新的解码方法
func get_summary() -> Dictionary:
    print("调用函数: get_summary()")
    
    var url = BASE_URL + "classes/_GameSave?limit=1"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取summary失败")
        return {}
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("results") or response["results"].size() == 0:
        push_error("无效的响应格式")
        return {}
    
    var result_data = response["results"][0]
    var summary_base64 = result_data["summary"]
    
    # 使用新的解码函数
    var summary_dict = decode_summary(summary_base64)
    
    # 添加额外信息
    summary_dict["checksum"] = result_data["gameFile"]["metaData"]["_checksum"]
    summary_dict["updateAt"] = result_data["updatedAt"]
    summary_dict["url"] = result_data["gameFile"]["url"]
    
    print('函数 "get_summary()" 返回: ', summary_dict)
    self.emit_signal("get_summary_success", summary_dict)
    return summary_dict

# 修改upload_summary函数使用新的编码方法
func upload_summary(summary: Dictionary, new_file_id: String = ""):
    print("调用函数: upload_summary()")
    
    # 使用新的编码函数
    var summary_base64 = encode_summary(summary)
    
    # 获取存档信息
    var url = BASE_URL + "classes/_GameSave?limit=1"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取存档信息失败")
        return
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("results") or response["results"].size() == 0:
        push_error("无效的响应格式")
        #return
    
    var save_info = response["results"][0]
    var object_id = save_info["objectId"]
    var user_id = save_info["user"]["objectId"]
    
    # 使用新文件ID（如果提供了），否则使用原始文件ID
    var file_id = new_file_id if new_file_id != "" else save_info["gameFile"]["objectId"]
    
    # 更新summary
    url = BASE_URL + "classes/_GameSave/%s?" % object_id
    var data = JSON.stringify({
        "summary": summary_base64,
        "modifiedAt": {
            "__type": "Date",
            "iso": Time.get_datetime_string_from_system(true) + "Z"
        },
        "gameFile": {
            "__type": "Pointer",
            "className": "_File",
            "objectId": file_id
        },
        "ACL": {user_id: {"read": true, "write": true}},
        "user": {
            "__type": "Pointer",
            "className": "_User",
            "objectId": user_id
        }
    })
    
    result = await _request(HTTPClient.METHOD_PUT, url, {"Content-Type": "application/json"}, data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("上传summary失败")
        return
    
    self.emit_signal("upload_summary_success")
    print("summary更新成功")

# 修改upload_summary函数使用新的编码方法
func upload_summary_FORCE(summary: Dictionary, new_file_id: String = ""):
    print("调用函数: upload_summary_FORCE()")
    
    # 使用新的编码函数
    var summary_base64 = encode_summary(summary)
    
    # 获取存档信息
    var url = BASE_URL + "classes/_GameSave?limit=1"
    var result
    
    var json = JSON.new()

    var Config = ConfigFile.new()
    var load_result = Config.load("user://config.cfg")
    var uuid = ""

    # 如果文件没有加载，忽略它。
    if load_result != OK:
        push_error("config.cfg 加载失败")
        return
    else:
        uuid = Config.get_value("Config", "uuid")
        if uuid == "" or uuid == null:
            push_error("config.cfg 未找到 uuid, 请在Android端初始化存档或获取UUID后继续。")
            return

    
    var user_id = uuid

    # 更新summary
    url = BASE_URL + "classes/_GameSave"
    var data = JSON.stringify({
        "name": "save",
        "summary": summary_base64,
        "modifiedAt": {
            "__type": "Date",
            "iso": Time.get_datetime_string_from_system(true) + "Z"
        },
        "gameFile": {
            "__type": "Pointer",
            "className": "_File",
            "objectId": new_file_id
        },
        "ACL": {user_id: {"read": true, "write": true}},
        "user": {
            "__type": "Pointer",
            "className": "_User",
            "objectId": user_id
        }
    })
    
    result = await _request(HTTPClient.METHOD_POST, url, {"Content-Type": "application/json"}, data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("上传summary失败")
        return
    
    self.emit_signal("upload_summary_success")
    print("summary创建成功")

# 获取存档数据并保存到文件
func get_save(custom_url: String = "", custom_checksum: String = "") -> PackedByteArray:
    print("调用函数: get_save()")
    
    var url = custom_url
    var checksum = custom_checksum
    
    if url == "" or checksum == "":
        var summary = await get_summary()
        if summary.is_empty():
            push_error("无法获取存档信息")
            return PackedByteArray()
        
        if url == "":
            url = summary["url"]
        if checksum == "":
            checksum = summary["checksum"]
    
    # 下载存档
    var result = await _request(HTTPClient.METHOD_GET, url)
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("下载存档失败")
        return PackedByteArray()
    
    var save_data: PackedByteArray = result[2]
    
    # 检查存档大小
    if save_data.size() <= 30:
        push_error("存档大小不足30字节! 当前大小: %d" % save_data.size())
        return PackedByteArray()
    
    ## 验证校验和
    #var md5 = save_data.md5_text()
    #if md5 != checksum:
        #push_error("存档校验失败! 本地: %s, 云端: %s" % [md5, checksum])
        #return PackedByteArray()
    
    # 保存到用户目录
    var file_path = "user://.save"
    var file = FileAccess.open(file_path, FileAccess.WRITE)
    if file == null:
        push_error("无法打开文件保存存档: %s" % file_path)
        return PackedByteArray()
    
    file.store_buffer(save_data)
    file.close()
    
    print("存档已保存到: %s" % file_path)
    print('函数 "get_save()" 返回: *%d bytes*' % save_data.size())
    self.emit_signal("get_save_success", save_data)
    return save_data

# 刷新session token
func refresh_session_token() -> String:
    print("调用函数: refresh_session_token()")
    
    # 获取objectId
    var url = BASE_URL + "users/me"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取用户信息失败")
        return ""
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("objectId"):
        push_error("无效的响应格式")
        return ""
    
    var object_id = response["objectId"]
    
    # 刷新token
    url = BASE_URL + "users/%s/refreshSessionToken" % object_id
    result = await _request(HTTPClient.METHOD_PUT, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("刷新token失败")
        return ""
    
    json.parse(result[2].get_string_from_utf8())
    response = json.get_data()
    
    if response == null or !response.has("sessionToken"):
        push_error("无效的token响应")
        return ""
    
    var new_token = response["sessionToken"]
    session_token = new_token
    headers["X-LC-Session"] = new_token
    
    print('函数 "refresh_session_token()" 返回: %s' % new_token)
    self.emit_signal("refresh_session_token_success", new_token)
    return new_token

# 更新玩家昵称
func upload_nickname(name: String):
    print("调用函数: upload_nickname(%s)" % name)
    
    # 获取用户ID
    var url = BASE_URL + "users/me"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取用户信息失败")
        return
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("objectId"):
        push_error("无效的响应格式")
        return
    
    var user_id = response["objectId"]
    
    # 更新昵称
    url = BASE_URL + "users/%s" % user_id
    var data = JSON.stringify({"nickname": name})
    result = await _request(HTTPClient.METHOD_PUT, url, {"Content-Type": "application/json"}, data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("更新昵称失败")
        return
    
    self.emit_signal("upload_nickname_success")
    
    print("昵称更新成功")
# 计算MD5校验和（使用HashingContext）
func _calculate_md5(data: PackedByteArray) -> String:
    var ctx = HashingContext.new()
    ctx.start(HashingContext.HASH_MD5)
    ctx.update(data)
    var hash_bytes = ctx.finish()
    return hash_bytes.hex_encode()

# 上传存档到云端
func upload_save(file_path: String = "user://.save"):
    print("调用函数: upload_save()")
    
    # 读取本地存档
    var file = FileAccess.open(file_path, FileAccess.READ)
    if file == null:
        push_error("无法打开存档文件: %s" % file_path)
        return
    
    var save_data = file.get_buffer(file.get_length())
    file.close()
    
    # 获取存档信息
    var url = BASE_URL + "classes/_GameSave?limit=1"
    var result = await _request(HTTPClient.METHOD_GET, url)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取存档信息失败")
        return
    
    var json = JSON.new()
    json.parse(result[2].get_string_from_utf8())
    var response = json.get_data()
    
    if response == null or !response.has("results"):
        push_error("无效的响应格式")
        return
    elif response["results"].size() == 0:
        # TODO: 创建新存档
        upload_save_FORCE()
    
    var save_info = response["results"][0]
    var object_id = save_info["objectId"]
    var user_id = save_info["user"]["objectId"]
    var old_file_id = save_info["gameFile"]["objectId"]
    var summary_old = response["results"][0]["summary"]
    
    # 验证校验和（使用新的HashingContext方法）
    var md5_hash = _calculate_md5(save_data)

    
    # 请求fileToken
    url = BASE_URL + "fileTokens"
    var token_data = JSON.stringify({
        "name": ".save",
        "__type": "File",
        "ACL": {user_id: {"read": true, "write": true}},
        "prefix": "gamesaves",
        "metaData": {
            "size": save_data.size(),
            "_checksum": md5_hash,
            "prefix": "gamesaves"
        }
    })
    
    result = await _request(HTTPClient.METHOD_POST, url, {"Content-Type": "application/json"}, token_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取fileToken失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var token_response = json.get_data()
    
    if token_response == null or !token_response.has("token") or !token_response.has("key") or !token_response.has("objectId"):
        push_error("无效的token响应")
        return
    
    var token_key = Marshalls.utf8_to_base64(token_response["key"])
    #var token_key = token_response["key"]
    var new_file_id = token_response["objectId"]
    var authorization = "UpToken " + token_response["token"]
    
    # 获取uploadId
    var upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads" % token_key
    result = await _request(HTTPClient.METHOD_POST, upload_url, {"Authorization": authorization})
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取uploadId失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var upload_response = json.get_data()
    
    if upload_response == null or !upload_response.has("uploadId"):
        push_error("无效的uploadId响应")
        return
    
    var upload_id = upload_response["uploadId"]
    
    # 上传存档
    upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads/%s/1" % [token_key, upload_id]
    result = await _request(HTTPClient.METHOD_PUT, upload_url, {
        "Authorization": authorization,
        "Content-Type": "application/octet-stream"
    }, save_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("上传存档失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var etag_response = json.get_data()
    
    if etag_response == null or !etag_response.has("etag"):
        push_error("无效的etag响应")
        return
    
    var etag = etag_response["etag"]
    
    # 完成上传
    upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads/%s" % [token_key, upload_id]
    var parts_data = JSON.stringify({"parts": [ {"partNumber": 1, "etag": etag}]})
    result = await _request(HTTPClient.METHOD_POST, upload_url, {
        "Authorization": authorization,
        "Content-Type": "application/json"
    }, parts_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("完成上传失败")
        return
    
    # 回调验证
    url = BASE_URL + "fileCallback"
    var callback_data = JSON.stringify({"result": true, "token": token_key})
    result = await _request(HTTPClient.METHOD_POST, url, {"Content-Type": "application/json"}, callback_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("回调验证失败")
        return
    
# 创建新summary，保留原数据只修改版本号
    var old_summary = decode_summary(summary_old)
    var new_summary = {
        "saveVersion": old_summary["saveVersion"], # 修改版本号
        "challenge": old_summary["challenge"],
        "rks": old_summary["rks"],
        "gameVersion": old_summary["gameVersion"],
        "avatar": old_summary["avatar"],
        "EZ": old_summary["EZ"],
        "HD": old_summary["HD"],
        "IN": old_summary["IN"],
        "AT": old_summary["AT"]
    }

    # 使用新创建的文件ID
    await upload_summary(new_summary, new_file_id)

    # 删除旧文件
    url = BASE_URL + "files/%s" % old_file_id
    result = await _request(HTTPClient.METHOD_DELETE, url)

    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("删除旧文件失败")
        return

    self.emit_signal("upload_save_success")
    print("存档上传成功")


# 强制上传存档到云端(云存档初始化)
func upload_save_FORCE(file_path: String = "user://.save"):
    print("调用函数: upload_save_FORCE()")
    
    # 读取本地存档
    var file = FileAccess.open(file_path, FileAccess.READ)
    if file == null:
        push_error("无法打开存档文件: %s" % file_path)
        return
    
    var save_data = file.get_buffer(file.get_length())
    file.close()
    
    # 获取存档信息
    var url = BASE_URL + "classes/_GameSave?limit=1"
    var result
    
    var json: JSON = JSON.new()
    
    var Config = ConfigFile.new()
    var load_result = Config.load("user://config.cfg")
    var uuid = ""

    # 如果文件没有加载，忽略它。
    if load_result != OK:
        push_error("config.cfg 加载失败")
        return
    else:
        uuid = Config.get_value("Config", "uuid")
        if uuid == "" or uuid == null:
            push_error("config.cfg 未找到 uuid, 请在Android端初始化存档或获取UUID后继续。")
            return

    
    var user_id = uuid
    
    # 验证校验和（使用新的HashingContext方法）
    var md5_hash = _calculate_md5(save_data)

    
    # 请求fileToken
    url = BASE_URL + "fileTokens"
    var token_data = JSON.stringify({
        "name": ".save",
        "__type": "File",
        "ACL": {user_id: {"read": true, "write": true}},
        "prefix": "gamesaves",
        "metaData": {
            "size": save_data.size(),
            "_checksum": md5_hash,
            "prefix": "gamesaves"
        }
    })
    
    result = await _request(HTTPClient.METHOD_POST, url, {"Content-Type": "application/json"}, token_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取fileToken失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var token_response = json.get_data()
    
    if token_response == null or !token_response.has("token") or !token_response.has("key") or !token_response.has("objectId"):
        push_error("无效的token响应")
        return
    
    var token_key = Marshalls.utf8_to_base64(token_response["key"])
    #var token_key = token_response["key"]
    var new_file_id = token_response["objectId"]
    var authorization = "UpToken " + token_response["token"]
    
    # 获取uploadId
    var upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads" % token_key
    result = await _request(HTTPClient.METHOD_POST, upload_url, {"Authorization": authorization})
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("获取uploadId失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var upload_response = json.get_data()
    
    if upload_response == null or !upload_response.has("uploadId"):
        push_error("无效的uploadId响应")
        return
    
    var upload_id = upload_response["uploadId"]
    
    # 上传存档
    upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads/%s/1" % [token_key, upload_id]
    result = await _request(HTTPClient.METHOD_PUT, upload_url, {
        "Authorization": authorization,
        "Content-Type": "application/octet-stream"
    }, save_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("上传存档失败")
        return
    
    json.parse(result[2].get_string_from_utf8())
    var etag_response = json.get_data()
    
    if etag_response == null or !etag_response.has("etag"):
        push_error("无效的etag响应")
        return
    
    var etag = etag_response["etag"]
    
    # 完成上传
    upload_url = "https://upload.qiniup.com/buckets/rAK3Ffdi/objects/%s/uploads/%s" % [token_key, upload_id]
    var parts_data = JSON.stringify({"parts": [ {"partNumber": 1, "etag": etag}]})
    result = await _request(HTTPClient.METHOD_POST, upload_url, {
        "Authorization": authorization,
        "Content-Type": "application/json"
    }, parts_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("完成上传失败")
        return
    
    # 回调验证
    url = BASE_URL + "fileCallback"
    var callback_data = JSON.stringify({"result": true, "token": token_key})
    result = await _request(HTTPClient.METHOD_POST, url, {"Content-Type": "application/json"}, callback_data)
    
    if result[0] != HTTPRequest.RESULT_SUCCESS:
        push_error("回调验证失败")
        return
    
# 创建新summary，保留原数据只修改版本号
    var new_summary = PhiSaveTools.generate_summary()
    await upload_summary_FORCE(new_summary, new_file_id)

    self.emit_signal("upload_save_success")
    print("存档上传成功")
