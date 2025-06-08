extends CanvasLayer

# 正确加载和调用 C# 类的方式
#var pack_save = PackSave.new()  # 创建 C# 类的实例
#var cloud_save = await CloudSave.new("nvbrl980tinzrv2cqv0wf936k",$"../")
var Phi_Save=PhiSave.new($".")
var cloud_save = Phi_Save.Cloud_Save
var old_id : String 

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


func _test_get():
    cloud_save.get_save()
    cloud_save.get_save_success.connect(_on_test_get_success)
    
func _on_test_get_success(data):
    PackSave.dePack("user://.save", "user://PhigrosSaves.json")
    
func _test_upload():
    PackSave.Pack("user://PhigrosSaves.json", "user://.save")
    cloud_save.upload_save()
    
func _file():
    $FileDialog.visible=true

func _ready() -> void:
    GodotTDS.on_login_return.connect(_on_test_return)
    GodotTDS.on_anti_addiction_return.connect(_on_test_return)
    
    
func _on_test_return(code : int, msg : String) -> void:
    $Code.text = str(code)
    $Text.text = msg
    push_warning(code,"-",msg)
    if code == GodotTDS.StateCode.AD_SPLASH_TIME_OVER:
        GodotTDS.dispose_splash_ad()
        
        
func _on_login_button_down() -> void:
    GodotTDS.login()
    

func _on_anti_addiction_button_down() -> void:
    GodotTDS.anti_addiction()



func _on_logout_button_down() -> void:
    GodotTDS.logout()



func _on_get_user_profile_button_down() -> void:
    $Text.text = GodotTDS.get_user_object_id()

func _on_submit_game_save_button_down() -> void:
    disconnect_all_connections(self,"on_game_save_return")
    GodotTDS.on_game_save_return.connect(_on_fetch_game_save_return)
    PackSave.Pack()
    if OS.has_feature("android"):
        await GodotTDS.fetch_game_saves()
    else:
#        TODO:输入sessiontoken
        cloud_save.upload_save()

func _on_fetch_game_save_return(code:int,msg:String) -> void:
    if msg != "{}" and code == 1017:
        var game_data : GodotTDS.GameSaveData = GodotTDS.GameSaveData.new()
        var summary : Dictionary = cloud_save.decode_summary(JSON.parse_string(msg)["list"][0]["summary"])
        old_id = JSON.parse_string(msg)["list"][0]["id"]
        disconnect_all_connections(self,"on_game_save_return")
        GodotTDS.on_game_save_return.connect(_on_delete_game_save)
#        TODO: 冲突检测
        game_data.save_name = ".save"
        game_data.summary = cloud_save.encode_summary(summary)
        game_data.game_file_path = "user://.save"
        game_data.modified_at = Time.get_unix_time_from_system() as int
        GodotTDS.submit_game_save(game_data)


func _on_delete_game_save(code:int,msg:String) -> void:
    if msg != "{}" and code == 1017 and (len(msg) == 24 or not msg.contains(" ")):
        GodotTDS.delete_game_save(old_id)

func _on_fetch_game_saves_button_down() -> void:
    disconnect_all_connections(self,"on_game_save_return")
    GodotTDS.on_game_save_return.connect(_on_get_game_saves)
    GodotTDS.fetch_game_saves()

func _on_get_game_saves(_code:int,msg:String) ->void:
    if msg == "Game save delete successful":
        return
#        shit
    var result = await cloud_save._request(HTTPClient.METHOD_GET, JSON.parse_string(msg)["list"][0]["gameFile"].replace("\\",""))
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


func _on_TDS_login_pressed() -> void:
    Phi_Save.TDS_login()


func _on_TDS_sync() -> void:
    Phi_Save.TDS_sync_save()

func _on_sessiontoken_login() -> void:
    Phi_Save.sessiontoken=$TabContainer/PhiSave/sessiontoken.text
    Phi_Save.SessionToken_login($TabContainer/PhiSave/sessiontoken.text)
    

func _on_sessiontoken_sync() -> void:
    Phi_Save.SessionToken_sync_save()
