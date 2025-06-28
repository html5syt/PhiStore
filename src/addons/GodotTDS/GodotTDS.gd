extends Node


const Config = preload("res://addons/GodotTDS/config.gd")
const StateCode = preload("res://addons/GodotTDS/state_code.gd")


# 登录相关操作的信号
signal on_login_return(code : int, msg : String)
# 防沉迷相关操作的信号
signal on_anti_addiction_return(code : int, msg : String)
# 云存档相关操作的信号
signal on_game_save_return(code : int, msg : String)


enum
{
    # 使用后台配置的默认朝向
    ORIENTATION_DEFAULT = 0,
    # 横屏
    ORIENTATION_LANDSCAPE = 1,
    # 竖屏
    ORIENTATION_PORTRAIT = 2,
    # 根据陀螺仪旋转
    ORIENTATION_SENSOR = 3
}

class GameSaveData:
    var save_name : String
    var summary : String
    # played_time 的单位为毫秒
    var played_time : int
    var progress_value : int
    # 存档封面图片的路径
    var cover_path : String
    # 存档文件的路径
    var game_file_path : String
    # modified_at 的值应该设置为对应 Date 的时间戳
    var modified_at : int
    
    
    
var _plugin_name : String = "GodotTdsPlugin"
var _plugin_singleton : Variant = null


func _ready() -> void:
    if Engine.has_singleton(_plugin_name):
        _plugin_singleton = Engine.get_singleton(_plugin_name)
        _plugin_singleton.init(
            Config.client_id, Config.client_token, Config.server_url,
            Config.media_id, Config.media_name, Config.media_key
        )
            
        _plugin_singleton.connect("onLogInReturn", _dont_call_on_login_return)
        _plugin_singleton.connect("onAntiAddictionReturn", _dont_call_on_anti_addiction_return)
        _plugin_singleton.connect("onGameSaveReturn", _dont_call_on_game_save_return)

        
# 在安卓平台输出日志
func push_log(msg : String, error : bool = false) -> void:
    _call_android_function("pushLog", [msg, error])
    
    
# 获取安卓平台的缓存路径
func get_cache_dir_path() -> String:
    var cache_dir_path : Variant = _call_android_function("getCacheDirPath")
    return "" if cache_dir_path == null else cache_dir_path
    
    
# 在安卓平台弹出一个吐司弹窗
func show_toast(msg : String) -> void:
    _call_android_function("showToast", [msg])
        
        
# 使用内建账户登录
func login() -> void:
    _call_android_function("logIn")
        
        
# 退出登录
func logout() -> void:
    _call_android_function("logOut")


# 判断当前用户是否登录
func is_logged_in() -> bool:
    var logged_in : Variant = _call_android_function("isLoggedIn")
    return false if logged_in == null else logged_in
        
        
# 防沉迷
func anti_addiction(userIdentifier : String = "") -> void:
    _call_android_function("antiAddiction",[userIdentifier])
        
    
    
# 得到当前登录用户的信息
func get_user_profile() -> Dictionary:
    var json_string : Variant = _call_android_function("getUserProfile")
    return {} if json_string == null else JSON.parse_string(json_string)
    
    
# 得到当前登录用户的 objectId
func get_user_object_id() -> String:
    var object_id : Variant = _call_android_function("getUserObjectId")
    return "" if object_id == null else object_id
    
# 将游戏数据提交到云存档
# 这是一个异步操作，请处理对应的信号以获取提交结果
func submit_game_save(data : GameSaveData) -> void:
    if not OS.has_feature("android"):
        push_warning("Only works on Android")
        return
        
    var image_cache_result : Array = _cache_image_get_path(data.cover_path)
    if image_cache_result[0] == false:
        push_log("Invalid image! Failed to cache image!", true)
        return
        
    var file_cache_result : Array = _cache_file_get_path(data.game_file_path)
    if file_cache_result[0] == false:
        push_log("Invalid file! Failed to cache file!", true)
        return
        
    _call_android_function("submitGameSave", [
        data.save_name, data.summary, data.played_time,
        data.progress_value, image_cache_result[1], file_cache_result[1], data.modified_at
    ])
    
    
# 获取当前登录用户的所有存档数据
# 这是一个异步操作，请处理对应的信号以获取返回数据
func fetch_game_saves() -> void:
    _call_android_function("fetchGameSaves")
    
    
# 删除指定 Id 的存档
# 存档的 Id 包含在通过 fetch_game_saves 函数返回的数据中
# 这是一个异步操作，请处理对应的信号以获取删除结果
func delete_game_save(game_save_id) -> void:
    _call_android_function("deleteGameSave", [game_save_id])
    
    
# Dont call these functions from outside
func _dont_call_on_login_return(code : int, msg : String) -> void:
    on_login_return.emit(code, msg)
    
    
func _dont_call_on_anti_addiction_return(code : int, msg : String) -> void:
    on_anti_addiction_return.emit(code, msg)
    
    
func _dont_call_on_game_save_return(code : int, msg : String) -> void:
    on_game_save_return.emit(code, msg)
    
    
func _json_to_array(json_string : Variant) -> Array:
    if json_string == null:
        return []
    var dict : Dictionary = JSON.parse_string(json_string)
    if dict.has("list"):
        return dict["list"]
    else:
        return []
        
        
func _generate_unique_filepath(id : int, extension : String) -> String:
    var date_str : String = Time.get_date_string_from_system()
    var time_str : String = Time.get_time_string_from_system().replace(":", "-")
    var prefix_str : String = date_str + "-" + time_str
    var unique_id : String = prefix_str + "_" + str(hash(id))
    var cache_dir : String = get_cache_dir_path()
    var cache_path : String = cache_dir + "/" + unique_id + "." + extension
    return cache_path
        
        
func _cache_file_get_path(file_path : String) -> Array:
    if not FileAccess.file_exists(file_path):
        return [false, null]
        
    var cache_path : String = _generate_unique_filepath(hash(file_path), file_path.get_extension())
    if FileAccess.file_exists(cache_path):
        return [true, cache_path]
        
    var input_file : FileAccess = FileAccess.open(file_path, FileAccess.READ)
    var output_file : FileAccess = FileAccess.open(cache_path, FileAccess.WRITE)
    output_file.store_string(input_file.get_as_text())
    
    return [true, cache_path]
        
        
func _cache_image_get_path(image_path : String) -> Array:
    var tex : Texture2D = load(image_path) as Texture2D
    var image : Image = tex.get_image()
    if image == null:
        return [false, null]
        
    var cache_path : String = _generate_unique_filepath(image.get_rid().get_id(), "png")
    if FileAccess.file_exists(cache_path):
        return [true, cache_path]
        
    var error : Error = image.save_png(cache_path)
    if error != OK:
        var err_msg = "Failed to saving the png image! Error: " + str(error)
        push_log(err_msg, true)
        push_log("Error file: " + cache_path, true)
    else:
        push_log("Save the png image successful: " + cache_path)
        
    return [true, cache_path]
        
        
func _call_android_function(android_func : String, args : Array = []) -> Variant:
    if not OS.has_feature("android"):
        push_warning("Only works on Android")
        return null
        
    if args.size() == 0:
        return _plugin_singleton.call(android_func)
    elif args.size() == 1:
        return _plugin_singleton.call(android_func, args[0])
    elif args.size() == 2:
        return _plugin_singleton.call(android_func, args[0], args[1])
    elif args.size() == 3:
        return _plugin_singleton.call(android_func, args[0], args[1], args[2])
    elif args.size() == 5:
        return _plugin_singleton.call(android_func,
            args[0], args[1], args[2], args[3], args[4])
    elif args.size() == 7:
        return _plugin_singleton.call(android_func,
            args[0], args[1], args[2], args[3], args[4], args[5], args[6])
    else:
        return null
        
