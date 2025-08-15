extends Node
class_name WebPicLoader

var file_list: PackedStringArray = []
var path: String = ""

func _init(directory_path:String) -> void:
    path = directory_path
    # 创建 DirAccess 实例
    var dir = DirAccess.open(directory_path)
    if not dir:
        push_error("无法打开目录: " + directory_path)
        #return get_random_file_from_directory("res://assets/pigeon-default/illustration/")
    
    # 获取目录中的所有文件
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

func load_image(name:String) -> Texture2D:
    return load(path.path_join(file_list[file_list.find(name)]))
