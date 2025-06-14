extends Node
class_name PhiSaveTools

# tools
# 断开指定对象上某个信号的所有连接
static func disconnect_all_connections(object: Object, signal_name: String) -> void:
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

static func download_save_file(url: String, Cloud_Save: CloudSave) -> void:
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

static func generate_summary():
    var save = JSON.parse_string(FileAccess.open("user://PhigrosSaves.json", FileAccess.READ).get_as_text())
    print(save)
    countRks(save["gameRecord"])
    # var summary = {
    #     "saveVersion": save_version,
    #     "challenge": challenge,
    #     "rks": rks,
    #     "gameVersion": game_version,
    #     "avatar": avatar,
    #     "EZ": ratings.slice(0, 3),
    #     "HD": ratings.slice(3, 6),
    #     "IN": ratings.slice(6, 9),
    #     "AT": ratings.slice(9, 12)
    # }
    #return Cloud_Save.encode_summary(summary)


# 构建难度字典的函数
static func load_difficulty_data() -> Dictionary:
    var difficulty_dict = {}
    var file_path = "res://assets/pigeon/info/difficulty.tsv"
    
    var file = FileAccess.open(file_path, FileAccess.READ)
    if file == null:
        push_error("无法打开难度文件: " + file_path)
        return difficulty_dict
    
    while not file.eof_reached():
        var line = file.get_line().strip_edges()
        if line == "":
            continue
            
        var parts = line.split("\t")
        if parts.size() < 4:  # 至少包含名称+3个难度
            push_warning("无效行: " + line)
            continue
        
        var song_name = parts[0]
        var diff_values = []
        
        for i in range(1, parts.size()):
            if parts[i].is_valid_float():
                diff_values.append(parts[i].to_float())
            else:
                push_warning("无效数值: " + parts[i] + " 在歌曲: " + song_name)
                diff_values.append(0.0)
        
        difficulty_dict[song_name] = diff_values
    
    file.close()
    return difficulty_dict

# 主处理函数
static func countRks(record_data: Dictionary, count_rks: bool = true) -> Dictionary:
    var diff_list = {"EZ": 0, "HD": 1, "IN": 2, "AT": 3, "Legacy": 4}
    var difficulty = load_difficulty_data()
    
    var gameRecord = record_data
    if record_data.has("gameRecord") and typeof(record_data["gameRecord"]) == TYPE_DICTIONARY:
        gameRecord = record_data["gameRecord"]
    
    for song_name in gameRecord:
        var song = gameRecord[song_name]
        for diff in song:
            var record_diff = 0.0
            var rks = 0.0
            
            if difficulty.has(song_name):
                var diff_index = diff_list.get(diff, -1)
                var diff_values = difficulty[song_name]
                
                if diff_index >= 0 and diff_index < diff_values.size():
                    record_diff = diff_values[diff_index]
                    
                    if count_rks:
                        var acc = song[diff].get("acc", 0.0)
                        if acc > 70:
                            rks = pow((acc - 55) / 45.0, 2) * record_diff
                else:
                    push_warning("歌曲 %s 的难度 %s 索引无效！可能为旧谱记录" % [song_name, diff])
            else:
                push_warning("歌曲 %s 的难度数据不存在！" % song_name)
            
            if count_rks:
                song[diff]["difficulty"] = record_diff
                song[diff]["rks"] = rks
            else:
                song[diff]["difficulty"] = record_diff
    
    return record_data

# func countRksAll(record_data: Dictionary) -> float:
