extends Node


func _ready() -> void:
    pass
#    连接回调信号
    GodotTDS.on_login_return.connect(_on_login_ok)

func init() -> void:
    pass
    
func login() -> void:
    GodotTDS.login()

func _on_login_ok() -> void:
    # 不同之处：立即同步存档
    GodotTDS.fetch_game_saves()
    GodotTDS.on_game_save_return.connect(_on_fetch_game_saves_ok)
    
func _on_fetch_game_saves_ok(code : int, msg : String) -> void:
    if msg == "{}":
        pass
    # 无存档
