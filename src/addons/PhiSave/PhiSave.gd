extends Node
class_name PhiSave

# tools
# 断开指定对象上某个信号的所有连接
func disconnect_all_connections(object: Object, signal_name: String) -> void:
    # 检查对象是否存在
    if !is_instance_valid(object):
        push_error("无效的对象实例")
        return
    
    # 获取信号的所有连接列表
    var connections: Array = object.get_signal_connection_list(signal_name)
    
    # 遍历并断开所有连接
    for connection in connections:
        var callable: Callable = connection["callable"]
        # 使用信号名和可调用对象断开连接
        object.disconnect(signal_name, callable)

func download_save_file(url: String) -> void:
    var result = await Cloud_Save._request(HTTPClient.METHOD_GET, url)
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
    PackSave.dePack()


var Config: ConfigFile
var sessiontoken: String
var old_id: String = ""
var Cloud_Save: CloudSave

signal on_TDS_login_success
signal on_TDS_sync_success
signal on_sessiontoken_login_success # sessiontoken登录成功
signal on_sessiontoken_sync_success # sessiontoken同步成功
signal require_sessiontoken # 由对应UI负责处理弹窗请求

func _init(parent: Node) -> void:
    Cloud_Save = CloudSave.new("", parent)
    Config = ConfigFile.new()
    var load_result = Config.load("user://config.cfg")

    # 如果文件没有加载，忽略它。
    if load_result != OK:
        push_warning("config.cfg 加载失败")
        Config = ConfigFile.new()
        sessiontoken = ""
    else:
        sessiontoken = Config.get_value("Config", "sessiontoken")
        Cloud_Save.headers["X-LC-Session"] = sessiontoken
        Cloud_Save.session_token = sessiontoken


# 存档初始化

func init() -> void:
    # 存档初始化
    pass

# TDS登录

func TDS_login() -> void:
    disconnect_all_connections(GodotTDS, "on_login_return")
    GodotTDS.on_login_return.connect(_on_TDS_login_return)
    GodotTDS.login()

func _on_TDS_login_return(code: int, msg: String) -> void:
    match code:
        GodotTDS.StateCode.LOG_IN_SUCCESS:
            sessiontoken = JSON.parse_string(msg)["sessionToken"]
            var shortId = JSON.parse_string(msg)["shortId"]
            var nickName = JSON.parse_string(msg)["nickName"]
            Config.set_value("Config", "sessiontoken", sessiontoken)
            Config.set_value("Config", "shortId", shortId)
            Config.set_value("Config", "nickName", nickName)
            Config.save("user://config.cfg")
            print("TDS登录成功")
            on_TDS_login_success.emit()
            TDS_sync_save()
        36869:
            print("TDS登录失败: 签名不匹配")
            require_sessiontoken.emit()
        _:
            print("TDS登录失败")
            require_sessiontoken.emit()
    # W 0:00:21:968   main.gd:28 @ _on_test_return(): 36869-signature not match
    # W 0:04:17:403   main.gd:28 @ _on_test_return(): 1001-{"uuid":"65befe9fd303d5cfc957b297","nickName":"0y2bvagsfvhf97xw7gpeh7a85","sessionToken":"nvbrl980tinzrv2cqv0wf936k","isMobilePhoneVerified":false,"isAuthenticated":true,"isAnonymous":false,"shortId":"6wn9yx","nickName":"Tim","avatar":"https:\/\/img3.tapimg.com\/default_avatars\/384aa197eceba6322c9af740d008e65e.jpg?imageMogr2\/auto-orient\/strip\/thumbnail\/!270x270r\/gravity\/Center\/crop\/270x270\/format\/jpg\/interlace\/1\/quality\/80","serverData":"{\"shortId\":\"6wn9yx\",\"createdAt\":\"2024-02-04T03:03:59.813Z\",\"nickname\":\"Tim\",\"mobilePhoneVerified\":false,\"objectId\":\"65befe9fd303d5cfc957b297\",\"updatedAt\":\"2025-06-08T02:46:52.220Z\",\"authData\":{\"taptap\":{\"access_token\":\"1\/SHJRD6MzYHtTaWn0qKRPfyOiNnWRsQNm1xL-cVy5bOkijMMoj36xXlIJIyDtVc8DzqlX1xEamRmh3rsKHCoVv_oNxqlI-Auj-omzC-XjnhB_SP7x4sptE3sEvK6ATo54hhCAgm3uqheac--yVU0DXkI-MbaebfNdo-hppFaA0S8rVW8HYnc8sx9mFP0Ws1-X91QiTNGxg4lUcET-PL6ob7Vqrp4aqh5Mrzz1PeqdTMtR48LwfSK5uwMPYr3-7SMqinlUS3_MCGz2DhTM7QV7UDhPAMYqU_a0uvGl4q4iuOPae6SuxxcVyoWy__AzSW-VouIyJ7ibElgPUnTFeMgecA\",\"mac_key\":\"3gOEn3iye8ihI76q1H6gXKmioOUyka3dtRbhfxqw\",\"mac_algorithm\":\"hmac-sha-1\",\"unionid\":\"V8m8GUCrGgKwtJAQJJ20Xw==\",\"openid\":\"LdzEmLgtUr5FhPYHMPWlDA==\",\"kid\":\"1\/SHJRD6MzYHtTaWn0qKRPfyOiNnWRsQNm1xL-cVy5bOkijMMoj36xXlIJIyDtVc8DzqlX1xEamRmh3rsKHCoVv_oNxqlI-Auj-omzC-XjnhB_SP7x4sptE3sEvK6ATo54hhCAgm3uqheac--yVU0DXkI-MbaebfNdo-hppFaA0S8rVW8HYnc8sx9mFP0Ws1-X91QiTNGxg4lUcET-PL6ob7Vqrp4aqh5Mrzz1PeqdTMtR48LwfSK5uwMPYr3-7SMqinlUS3_MCGz2DhTM7QV7UDhPAMYqU_a0uvGl4q4iuOPae6SuxxcVyoWy__AzSW-VouIyJ7ibElgPUnTFeMgecA\",\"name\":\"Html5syt\",\"avatar\":\"https:\/\/img3.tapimg.com\/default_avatars\/384aa197eceba6322c9af740d008e65e.jpg?imageMogr2\/auto-orient\/strip\/thumbnail\/!270x270r\/gravity\/Center\/crop\/270x270\/format\/jpg\/interlace\/1\/quality\/80\",\"token_type\":\"mac\"}},\"ACL\":{\"*\":{\"write\":true,\"read\":true}},\"avatar\":\"https:\/\/img3.tapimg.com\/default_avatars\/384aa197eceba6322c9af740d008e65e.jpg?imageMogr2\/auto-orient\/strip\/thumbnail\/!270x270r\/gravity\/Center\/crop\/270x270\/format\/jpg\/interlace\/1\/quality\/80\",\"emailVerified\":false,\"sessionToken\":\"nvbrl980tinzrv2cqv0wf936k\",\"nickName\":\"0y2bvagsfvhf97xw7gpeh7a85\"}"}

# TDS存档同步
func TDS_sync_save() -> void:
    var updateTime = Config.get_value("Save", "UpdateTime")
    if updateTime != null:
        disconnect_all_connections(GodotTDS, "on_game_save_return")
        GodotTDS.on_game_save_return.connect(_on_TDS_fetch_game_saves_return)
        GodotTDS.fetch_game_saves()
    else:
        # 未获取过存档
        disconnect_all_connections(GodotTDS, "on_game_save_return")
        GodotTDS.on_game_save_return.connect(func(code: int, msg: String): download_save_file(JSON.parse_string(msg)["list"][0]["gameFile"].replace("\\", "")) if code == GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS else push_error("下载存档失败"))
        GodotTDS.fetch_game_saves()

func _on_TDS_fetch_game_saves_return(code: int, msg: String) -> void:
    var updateTime = Config.get_value("Save", "UpdateTime")
    match code:
        GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS:
            var save: Dictionary = JSON.parse_string(msg)["list"][0]
            var id = save["id"]
            var summary = save["summary"]
            var modifiedAt = save["modifiedAt"]
            var gameFile: String = save["gameFile"].replace("\\", "")
            if modifiedAt > updateTime:
                # 下载存档
                download_save_file(gameFile)
            else:
                # 上传存档
                var game_data: GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
                old_id = JSON.parse_string(msg)["list"][0]["id"]
                disconnect_all_connections(self, "on_game_save_return")
                GodotTDS.on_game_save_return.connect(_delete_game_save_after_upload)
        #        TODO: 冲突检测
                game_data.save_name = ".save"
                game_data.summary = Cloud_Save.encode_summary(summary)
                game_data.game_file_path = "user://.save"
                game_data.modified_at = Time.get_unix_time_from_system() as int
                Config.set_value("Save", "UpdateTime", modifiedAt)
                Config.save("user://config.cfg")
                GodotTDS.submit_game_save(game_data)

func _delete_game_save_after_upload(code: int, msg: String) -> void:
    match code:
        GodotTDS.StateCode.GAME_SAVE_SUBMIT_SUCCESS:
            Config.set_value("Save", "UpdateTime", Time.get_unix_time_from_system() as int)
            Config.save("user://config.cfg")
            print("存档上传成功")
            disconnect_all_connections(GodotTDS, "on_game_save_return")
            GodotTDS.on_game_save_return.connect(func(): on_TDS_sync_success.emit())
            await get_tree().create_timer(0.5).timeout
            GodotTDS.delete_game_save(old_id)

# sessiontoken登录

func SessionToken_login(session_token: String) -> void:
    Cloud_Save.session_token = session_token
    Cloud_Save.headers["X-LC-Session"] = session_token
    var nickName = await Cloud_Save.get_nickname()
    Config.set_value("Config", "sessiontoken", session_token)
    Config.set_value("Config", "shortId", "session")
    Config.set_value("Config", "nickName", nickName)
    SessionToken_sync_save()
    on_sessiontoken_login_success.emit()

func SessionToken_sync_save() -> void:
    var updateTime = Config.get_value("Save", "UpdateTime")
    if updateTime != null:
        var updateAt = await Cloud_Save.get_summary()
        updateAt = Time.get_unix_time_from_datetime_string((updateAt["updateAt"]))*1000
        if updateAt > updateTime:
            # 下载存档
            await Cloud_Save.get_save()
            PackSave.dePack()
        else:
            # 上传存档
            Cloud_Save.update_save()
    else:
        await Cloud_Save.get_save()
        PackSave.dePack()
    on_sessiontoken_sync_success.emit()
