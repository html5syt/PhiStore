extends Node
class_name PhiSave


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
            var uuid = JSON.parse_string(msg)["uuid"]
            Config.set_value("Config", "sessionToken", sessiontoken)
            Config.set_value("Config", "shortId", shortId)
            Config.set_value("Config", "nickName", nickName)
            Config.set_value("Config", "uuid", uuid)
            # Config.set_value("SaveInfo", "updateTime", 0)
            Config.save("user://config.cfg")
            print("TDS登录成功!")
            on_TDS_login_success.emit()
            # TDS_sync_save()
            SessionToken_login(sessiontoken)
        36869:
            print("TDS登录失败: 签名不匹配!")
            require_sessiontoken.emit()
        _:
            print("TDS登录失败!")
            require_sessiontoken.emit()

# TDS存档同步
func TDS_sync_save() -> void:
    SessionToken_sync_save()
#     var updateTime = Config.get_value("SaveInfo", "updateTime")
#     if updateTime != null:
#         PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
#         GodotTDS.on_game_save_return.connect(_on_TDS_fetch_game_saves_return)
#         GodotTDS.fetch_game_saves()
#     else:
#         # 未获取过存档
#         PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
#         var RETURN = func(code: int, msg: String):
#             match code:
#                 GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS:
#                     if msg != "{}":
#                         await PhiSaveTools.download_save_file(JSON.parse_string(msg)["list"][0]["gameFile"].replace("\\", ""),Cloud_Save)
#                     elif msg == "{}":
#                         # 强制上传本地存档
#                         TDS_upload_save(true)
#                     else:
#                         push_error("下载存档失败")
#         GodotTDS.on_game_save_return.connect(RETURN)
#         GodotTDS.fetch_game_saves()

# func _on_TDS_fetch_game_saves_return(code: int, msg: String) -> void:
#     var updateTime = Config.get_value("SaveInfo", "updateTime")
#     var save: Dictionary = JSON.parse_string(msg)["list"][0]
#     match code:
#         GodotTDS.StateCode.GAME_SAVE_FETCH_SUCCESS:
#             var modifiedAt = save["modifiedAt"]
#             var gameFile: String = save["gameFile"].replace("\\", "")
#             if modifiedAt > updateTime:
#                 # 下载存档
#                 PhiSaveTools.download_save_file(gameFile,Cloud_Save)
#             else:
#                 TDS_upload_save(msg)
# func _delete_game_save_after_upload(code: int, msg: String) -> void:
#     match code:
#         GodotTDS.StateCode.GAME_SAVE_SUBMIT_SUCCESS:
#             Config.set_value("SaveInfo", "updateTime", Time.get_unix_time_from_system())
#             Config.save("user://config.cfg")
#             print("存档上传成功")
#             PhiSaveTools.disconnect_all_connections(GodotTDS, "on_game_save_return")
#             GodotTDS.on_game_save_return.connect(func(): on_TDS_sync_success.emit())
#             await get_tree().create_timer(0.5).timeout
#             GodotTDS.delete_game_save(old_id)

# # TDS上传存档

func TDS_upload_save(Force = false,msg: String = "") -> void:
    Cloud_Save.upload_save()
#     if not Force and msg != "":
#         # 上传存档
#         var save: Dictionary = JSON.parse_string(msg)["list"][0]
#         var id = save["id"]
#         var summary = save["summary"]
#         var modifiedAt = save["modifiedAt"]
#         var gameFile: String = save["gameFile"].replace("\\", "")
#         var game_data: GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
#         old_id = JSON.parse_string(msg)["list"][0]["id"] if not Force else null
#         PhiSaveTools.disconnect_all_connections(self, "on_game_save_return")
#         GodotTDS.on_game_save_return.connect(_delete_game_save_after_upload)
# #        TODO: 冲突检测
#         game_data.save_name = ".save"
#         game_data.summary = Cloud_Save.encode_summary(summary)
#         game_data.game_file_path = "user://.save"
#         game_data.modified_at = Time.get_unix_time_from_system() as int
#         Config.set_value("SaveInfo", "updateTime", modifiedAt)
#         Config.save("user://config.cfg")
#         print("23454")
#         GodotTDS.submit_game_save(game_data)
#     else:
#         # 强制上传存档
#         var save: Dictionary = JSON.parse_string(msg)["list"][0]
#         var id = save["id"]
#         var summary = save["summary"]
#         var modifiedAt = save["modifiedAt"]
#         var gameFile: String = save["gameFile"].replace("\\", "")
#         var game_data: GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
#         old_id = JSON.parse_string(msg)["list"][0]["id"] if not Force else null
#         PhiSaveTools.disconnect_all_connections(self, "on_game_save_return")
#         GodotTDS.on_game_save_return.connect(_delete_game_save_after_upload)
# #        TODO: 冲突检测
#         game_data.save_name = ".save"
#         game_data.summary = Cloud_Save.encode_summary(summary)
#         game_data.game_file_path = "user://.save"
#         game_data.modified_at = Time.get_unix_time_from_system() as int
#         Config.set_value("SaveInfo", "updateTime", modifiedAt)
#         Config.save("user://config.cfg")
#         print("23454")
#         GodotTDS.submit_game_save(game_data)

# TDS下载存档

# sessiontoken登录

func SessionToken_login(session_token: String) -> void:
    Cloud_Save.session_token = session_token
    Cloud_Save.headers["X-LC-Session"] = session_token
    var nickName = await Cloud_Save.get_nickname()
    Config.set_value("Config", "sessionToken", session_token)
    Config.set_value("Config", "shortId", "session")
    Config.set_value("Config", "nickName", nickName)
    Config.save("user://config.cfg")
    SessionToken_sync_save()
    on_sessiontoken_login_success.emit()

func SessionToken_sync_save() -> void:
    var updateTime = Config.get_value("SaveInfo", "updateTime")
    if updateTime != null:
        var updateAt = await Cloud_Save.get_summary()
        if updateAt.size() == 0:
            await Cloud_Save.upload_save_FORCE()
            return
        updateAt = Time.get_unix_time_from_datetime_string((updateAt["updateAt"]))
        if updateAt > updateTime:
            # 下载存档
            await Cloud_Save.get_save()
            PackSave.dePack()
        else:
            # 上传存档
            await Cloud_Save.upload_save()
    else:
        await Cloud_Save.get_save()
        PackSave.dePack()
    Config.set_value("SaveInfo", "updateTime", Time.get_unix_time_from_system() as int)
    Config.save("user://config.cfg")
    on_sessiontoken_sync_success.emit()
