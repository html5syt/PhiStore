extends Node
class_name PhiSave

# var PhiSaveTools = load("res://addons/PhiSave/PhiSaveTools.gd").new()

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
        sessiontoken = Config.get_value("Config", "sessionToken")
        Cloud_Save.headers["X-LC-Session"] = sessiontoken
        Cloud_Save.session_token = sessiontoken


# 存档初始化

func init() -> void:
    # 存档初始化
    pass

# TDS登录

func TDS_login() -> void:
    PhiSaveTools.disconnect_all_connections(GodotTDS, "on_login_return")
    GodotTDS.on_login_return.connect(_on_TDS_login_return)
    GodotTDS.login()

func _on_TDS_login_return(code: int, msg: String) -> void:
    match code:
        GodotTDS.StateCode.LOG_IN_SUCCESS:
            sessiontoken = JSON.parse_string(msg)["sessionToken"]
            var shortId = JSON.parse_string(msg)["shortId"]
            var nickName = JSON.parse_string(msg)["nickName"]
            Config.set_value("Config", "sessionToken", sessiontoken)
            Config.set_value("Config", "shortId", shortId)
            Config.set_value("Config", "nickName", nickName)
            Config.save("user://config.cfg")
            print("TDS登录成功!")
            on_TDS_login_success.emit()
            TDS_sync_save()
        36869:
            print("TDS登录失败: 签名不匹配!")
            require_sessiontoken.emit()
        _:
            print("TDS登录失败!")
            require_sessiontoken.emit()

# TDS存档同步
func TDS_sync_save() -> void:
    var updateTime = Config.get_value("SaveInfo", "updateTime")
    if updateTime != null:
        PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
        GodotTDS.on_game_save_return.connect(_on_TDS_fetch_game_saves_return)
        GodotTDS.fetch_game_saves()
    else:
        # 未获取过存档
        PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
        var RETURN = func(code: int, msg: String):
            match code:
                GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS:
                    if msg != "{}":
                        await PhiSaveTools.download_save_file(JSON.parse_string(msg)["list"][0]["gameFile"].replace("\\", ""),Cloud_Save)
                    elif msg == "{}":
                        # 强制上传本地存档
                        TDS_upload_save()
                    else:
                        push_error("下载存档失败")
        GodotTDS.on_game_save_return.connect(RETURN)
        GodotTDS.fetch_game_saves()

func _on_TDS_fetch_game_saves_return(code: int, msg: String) -> void:
    var updateTime = Config.get_value("SaveInfo", "updateTime")
    var save: Dictionary = JSON.parse_string(msg)["list"][0]
    match code:
        GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS:
            var modifiedAt = save["modifiedAt"]
            var gameFile: String = save["gameFile"].replace("\\", "")
            if modifiedAt > updateTime:
                # 下载存档
                PhiSaveTools.download_save_file(gameFile,Cloud_Save)
            else:
                TDS_upload_save()
func _delete_game_save_after_upload(code: int, msg: String) -> void:
    match code:
        GodotTDS.StateCode.GAME_SAVE_SUBMIT_SUCCESS:
            Config.set_value("SaveInfo", "updateTime", Time.get_unix_time_from_system())
            Config.save("user://config.cfg")
            print("存档上传成功")
            PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
            GodotTDS.on_game_save_return.connect(func(): on_TDS_sync_success.emit())
            await get_tree().create_timer(0.5).timeout
            GodotTDS.delete_game_save(old_id)

# TDS上传存档

func TDS_upload_save(Force = false,msg: String = "") -> void:
    if not Force and msg != "":
        # 上传存档
        var save: Dictionary = JSON.parse_string(msg)["list"][0]
        var id = save["id"]
        var summary = save["summary"]
        var modifiedAt = save["modifiedAt"]
        var gameFile: String = save["gameFile"].replace("\\", "")
        var game_data: GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
        old_id = JSON.parse_string(msg)["list"][0]["id"] if not Force else null
        PhiSaveTools.disconnect_all_connections(self, "on_game_save_return")
        GodotTDS.on_game_save_return.connect(_delete_game_save_after_upload)
#        TODO: 冲突检测
        game_data.save_name = ".save"
        game_data.summary = Cloud_Save.encode_summary(summary)
        game_data.game_file_path = "user://.save"
        game_data.modified_at = Time.get_unix_time_from_system() as int
        Config.set_value("SaveInfo", "updateTime", modifiedAt)
        Config.save("user://config.cfg")
        print("23454")
        GodotTDS.submit_game_save(game_data)
    else:
        # 上传存档
        var save: Dictionary = JSON.parse_string(msg)["list"][0]
        var id = save["id"]
        var summary = save["summary"]
        var modifiedAt = save["modifiedAt"]
        var gameFile: String = save["gameFile"].replace("\\", "")
        var game_data: GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
        old_id = JSON.parse_string(msg)["list"][0]["id"] if not Force else null
        PhiSaveTools.disconnect_all_connections(self, "on_game_save_return")
        GodotTDS.on_game_save_return.connect(_delete_game_save_after_upload)
#        TODO: 冲突检测
        game_data.save_name = ".save"
        game_data.summary = Cloud_Save.encode_summary(summary)
        game_data.game_file_path = "user://.save"
        game_data.modified_at = Time.get_unix_time_from_system() as int
        Config.set_value("SaveInfo", "updateTime", modifiedAt)
        Config.save("user://config.cfg")
        print("23454")
        GodotTDS.submit_game_save(game_data)

# TDS下载存档

# sessiontoken登录

func SessionToken_login(session_token: String) -> void:
    Cloud_Save.session_token = session_token
    Cloud_Save.headers["X-LC-Session"] = session_token
    var nickName = await Cloud_Save.get_nickname()
    Config.set_value("Config", "sessionToken", session_token)
    Config.set_value("Config", "shortId", "session")
    Config.set_value("Config", "nickName", nickName)
    SessionToken_sync_save()
    on_sessiontoken_login_success.emit()

func SessionToken_sync_save() -> void:
    var updateTime = Config.get_value("SaveInfo", "updateTime")
    if updateTime != null:
        var updateAt = await Cloud_Save.get_summary()
        updateAt = Time.get_unix_time_from_datetime_string((updateAt["updateAt"])) * 1000
        if updateAt > updateTime:
            # 下载存档
            await Cloud_Save.get_save()
            PackSave.dePack()
        else:
            # 上传存档
            Cloud_Save.update_save()
            Config.set_value("SaveInfo", "updateTime", Time.get_unix_time_from_system() as int)
    else:
        await Cloud_Save.get_save()
        PackSave.dePack()
    on_sessiontoken_sync_success.emit()
