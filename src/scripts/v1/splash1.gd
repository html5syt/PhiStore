extends Node

signal load_complete

func get_random_file_from_directory(directory_path: String) -> String:
    # 创建 DirAccess 实例
    var dir = DirAccess.open(directory_path)
    if not dir:
        push_error("无法打开目录: " + directory_path)
        #return ""
        return get_random_file_from_directory("res://assets/pigeon-default/illustration/")
    
    # 获取目录中的所有文件
    var file_list: PackedStringArray = []
    dir.list_dir_begin() # 开始遍历
    
    var file_name = dir.get_next()
    while file_name != "":
        file_name = dir.get_next()
        if not dir.current_is_dir(): # 确保是文件而非文件夹
            file_list.append(file_name.replace(".import", "")) if ".import" in file_name else file_list.append(file_name) # 排除.import 文件
        file_name = dir.get_next()
    
    dir.list_dir_end() # 结束遍历
    
    # 检查是否找到文件
    if file_list.is_empty():
        push_warning("目录中没有文件: " + directory_path)
        #return ""
        return get_random_file_from_directory("res://assets/pigeon-default/illustration/")
    
    # 随机选择并返回完整路径
    var random_index = randi() % file_list.size()
    return directory_path.path_join(file_list[random_index])
    
func _ready() -> void:
    $bgLoop.play()
    loading()

func loading() -> void:
    var bg := get_random_file_from_directory("res://assets/pigeon/illustration/")
    # 检查存档文件是否存在
    if not FileAccess.file_exists("user://PhigrosSaves.json"):
        PhiSave.init()
    await get_tree().create_timer(2.0).timeout
    $bgPic.texture = load(bg)
    load_complete.emit()

func _on_bg_loop_play_finished() -> void:
    $bgLoop.play()

func _on_load_complete() -> void:
    $bgPic/AnimationPlayer.play(&"fade_in")
    $TouchToStart/Label.text = "touch to start"
    $START.visible = true


func _on_press_start() -> void:
    await $TransitionManager.transition_to("res://scenes/v1/shop/shop.tscn")
