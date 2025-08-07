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

static func generate_summary() -> Dictionary:
    var save = JSON.parse_string(FileAccess.open("user://PhigrosSaves.json", FileAccess.READ).get_as_text())
    var ratings: Dictionary = {"EZ":[0, 3, 0], "HD":[0, 1, 0], "IN":[0, 0, 0], "AT":[0, 0, 0]}
#    数据统计疑似错误
    for song in save["gameRecord"]:
        for diff in save["gameRecord"][song]:
            # clear
            if save["gameRecord"][song][diff]["score"] > 0 and save["gameRecord"][song][diff]["acc"] > 0:
                ratings[diff][0] += 1
            # full combo
            if save["gameRecord"][song][diff]["fc"] == 1:
                ratings[diff][1] += 1
            # φ
            if save["gameRecord"][song][diff]["score"] == 1000000 and save["gameRecord"][song][diff]["acc"] == 100.0:
                ratings[diff][2] += 1
    var summary = {
        "saveVersion": ProjectSettings.get_setting("application/ExConfig/SaveVersion"),
        "challenge": save["gameProgress"]["challengeModeRank"],
        "rks": countRksAll(save["gameRecord"]),
        "gameVersion": ProjectSettings.get_setting("application/ExConfig/GameVersionInt"),
        "avatar": save["user"]["avatar"],
        "EZ": ratings["EZ"],
        "HD": ratings["HD"],
        "IN": ratings["IN"],
        "AT": ratings["AT"]
    }
    return summary
    
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
        if parts.size() < 4: # 至少包含名称+3个难度
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

static func countRksAll(record_data: Dictionary) -> float:
    var record_data_processed = countRks(record_data, true)
    var total_rks = 0.0
    var rks_list = []
    var rks_phi_list = []
    
    for song_name in record_data_processed:
        var songInfo = record_data_processed[song_name]
        for diff in songInfo:
            var rks = songInfo[diff]["rks"]
            if rks > 0.0:
                rks_list.append(rks)
                if songInfo[diff]["acc"] == 100.0 and songInfo[diff]["fc"] == 1.0 and songInfo[diff]["score"] == 1000000.0: # φ
                    rks_phi_list.append(rks)
    # 计算RKS
    rks_list.sort()
    rks_phi_list.sort()
    rks_list = rks_list.slice(-1, -28, -1)
    rks_phi_list = rks_phi_list.slice(-1, -4, -1)
    var rks = 0.0
    var rks_phi = 0.0
    for _rks in rks_list:
        rks += _rks
    for _rks in rks_phi_list:
        rks_phi += _rks
    total_rks = (rks + rks_phi) / (rks_list.size() + rks_phi_list.size())
    return round(total_rks * pow(10.0, 2)) / pow(10.0, 2) # 保留两位小数

static func parse_tsv_data(column_names: Array, file_path: String) -> Dictionary:
    var result = {}
    var file = FileAccess.open(file_path, FileAccess.READ)
    
    if not file:
        push_error("Failed to open file: " + file_path)
        return result
    
    # 跳过BOM（如果存在）
    if file.get_position() == 0 and file.get_8() == 0xEF:
        file.get_16()  # 跳过完整的BOM (EF BB BF)
    else:
        file.seek(0)  # 重置到文件开头
    
    while not file.eof_reached():
        var line = file.get_line().strip_edges()
        if line == "":
            continue
        
        var columns = line.split("\t")
        if columns.size() < 1:
            continue
        
        var song_id = columns[0]
        var song_data = {}
        
        # 为每个列名填充数据（跳过songID列）
        for col_idx in range(1, column_names.size()):
            var col_name = column_names[col_idx]
            # 检查数据是否存在，否则用空字符串填充
            song_data[col_name] = columns[col_idx] if col_idx < columns.size() else null
        
        if "," in song_id:
            continue  # 跳过第一行标题行
        result[song_id] = song_data
    
    file.close()
    return result

static func parse_list_string(list_str: String) -> Array:
    list_str = list_str.substr(1, list_str.length() - 2)  # 去掉首尾的括号
    var listO = list_str.strip_edges().replace(" ", "").split(",")
    var list = []
    for i in range(listO.size()):
        list.append(int(listO[i]))
    return list

static func concat_flag_and_type(flag,type) -> Array:
    var output = []
    var flagi = 0
    for i in type:
        if int(i) == 1:
            output.append(flag[flagi])
            flagi += 1
        else:
            output.append(0)
    return output

static func split_flag_and_type(output) -> Array:
    var flag = []
    var type = []
    for i in output:
        if int(i) >= 1:
            flag.append(int(i))
            type.append(int(i))
        else:
            type.append(0)
    return [flag, type]

class DataSizeConverter:
        # 单位定义
    const UNITS = ["KB", "MB", "GB", "TB", "PB"]
    const UNIT_FACTORS = [1, 1024, 1048576, 1073741824, 1099511627776]  # 1024^0, 1024^1, 1024^2, 1024^3, 1024^4


    # 将数组转换为最高单位表示法
    static func convert_to_highest(sizes: Array) -> Array:
        # 1. 计算总KB数
        var total_kb = convert_to_kb(sizes)
        
        # 2. 特殊情况处理
        if total_kb == 0:
            return [0.00, "KB"]
        
        # 3. 自动选择最佳单位
        var value = float(total_kb)
        var unit_index = 0
        var max_unit_index = UNITS.size() - 1
        
        while value >= 1024.00 and unit_index < max_unit_index:
            value /= 1024.00
            unit_index += 1
        
        # 4. 格式化结果
        return [round(value * 100) / 100.00, UNITS[unit_index]]

    # 将KB整数转换回数组形式 [KB, MB, GB, TB, PB]
    static func convert_from_kb(total_kb: int) -> Array:
        var result = [0, 0, 0, 0, 0]  # 初始化结果数组
        
        # 从最高单位(PB)开始向下计算
        for i in range(4, -1, -1):
            if UNIT_FACTORS[i] <= total_kb:
                result[i] = total_kb / UNIT_FACTORS[i]
                total_kb = total_kb % UNIT_FACTORS[i]
        
        return result

    # 将数组转换为KB整数
    static func convert_to_kb(sizes: Array) -> int:
        var total_kb = 0
        for i in range(sizes.size()):
            total_kb += sizes[i] * pow(1024, i)
        return int(total_kb)

    static func convert_from_highest(highest_data: String) -> Array:
        # highest_data格式：[数值, 单位]
        highest_data = highest_data.strip_edges().replace(" ", "")
        var value = highest_data.substr(0, highest_data.length()-2).to_float()
        var unit = highest_data.substr(highest_data.length()-2)
        
        # 获取单位索引
        var unit_index = UNITS.find(unit)
        if unit_index == -1:
            push_error("无效单位: " + unit)
            return [0, 0, 0, 0, 0]
        
        # 将值转换为KB
        var total_kb = int(round(value * pow(1024, unit_index)))
        
        # 使用现有的转换方法
        return convert_from_kb(total_kb)

static func weighted_random(weights: Dictionary):
    # 计算总权重
    # ep
    # {
    #     "普通剑": 60.0,
    #     "魔法杖": 30.0,
    #     "传奇武器": 9.8,
    #     "神器": 0.2
    # }
    var total_weight: float = 0.0
    for item in weights:
        var w = weights[item]
        if w < 0:
            push_error("权重值不能为负数: " + str(item))
            return null
        total_weight += w
    
    # 处理总权重为0的情况
    if total_weight <= 0:
        push_error("总权重必须大于0")
        return null
    
    # 生成随机数 (0 ~ total_weight)
    var rnd = randf_range(0.0, total_weight)
    
    # 遍历选择元素
    var cumulative = 0.0
    for item in weights:
        cumulative += weights[item]
        if rnd <= cumulative:
            return item
    
    # 理论上不会执行到这里
    return null

static func get_avatar_path(avatarName:String) -> String:
    if avatarName == "":
        avatarName = "Introduction"
    var avatarPath = "res://assets/pigeon/avatar/%s.png" % avatarName if OS.has_feature("editor") else "res://assets/pigeon/avatar/%s.png.import" % avatarName
    # fuck special name
    match avatarName:
        "Cipher : /2&//<|0":
            avatarPath = "res://assets/pigeon/avatar/%s.png" % "Cipher1" if OS.has_feature("editor") else "res://assets/pigeon/avatar/%s.png.import" % "Cipher1"
    return avatarPath
