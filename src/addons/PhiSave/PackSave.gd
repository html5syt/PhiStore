extends RefCounted

class_name PackSave

# 存档文件列表
const SAVE_LIST = [
    "gameKey",
    "gameProgress",
    "gameRecord",
    "settings",
    "user"
]

# AES密钥和初始化向量
const AES_KEY = "6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks="
const AES_IV = "Kk/wisgNYwcAV8WVGMgyUw=="

# 解压存档
static func unzip_save(zip_path: String) -> Dictionary:
    var save_dict = {}
    
    if not FileAccess.file_exists(zip_path):
        push_error("存档文件不存在: " + zip_path)
        return {}
    
    var zip_reader = ZIPReader.new()
    var err = zip_reader.open(zip_path)
    if err != OK:
        push_error("无法打开ZIP文件: " + str(err))
        return {}
    
    for file in SAVE_LIST:
        if file in zip_reader.get_files():
            var data = zip_reader.read_file(file)
            if data.size() > 0:
                save_dict[file] = data
                print("解压文件: " + file)
            else:
                push_warning("空文件: " + file)
        else:
            push_warning("缺少文件: " + file)
    
    zip_reader.close()
    print("解压完成")
    return save_dict

# 压缩存档
static func zip_save(save_dict: Dictionary, output_path: String) -> bool:
    var zip_writer = ZIPPacker.new()
    var err = zip_writer.open(output_path)
    if err != OK:
        push_error("无法创建ZIP文件: " + str(err))
        return false
    
    for file in SAVE_LIST:
        if save_dict.has(file):
            zip_writer.start_file(file)
            zip_writer.write_file(save_dict[file])
            print("压缩文件: " + file)
        else:
            push_warning("跳过缺失文件: " + file)
    
    zip_writer.close_file()
    zip_writer.close()
    print("压缩完成")
    return true

# AES加密
static func encrypt(data: PackedByteArray) -> PackedByteArray:
    var ctx = AESContext.new()
    var key = Marshalls.base64_to_raw(AES_KEY)
    var iv = Marshalls.base64_to_raw(AES_IV)
    
    # PKCS7填充
    var block_size = 16
    var pad_len = block_size - (len(data) % block_size)
    var padded = data.duplicate()
    for i in range(pad_len):
        padded.append(pad_len)
    
    ctx.start(AESContext.MODE_CBC_ENCRYPT, key, iv)
    return ctx.update(padded)

# AES解密
static func decrypt(data: PackedByteArray) -> PackedByteArray:
    var ctx = AESContext.new()
    var key = Marshalls.base64_to_raw(AES_KEY)
    var iv = Marshalls.base64_to_raw(AES_IV)
    
    ctx.start(AESContext.MODE_CBC_DECRYPT, key, iv)
    var decrypted = ctx.update(data)
    
    # 移除PKCS7填充
    var pad_len = decrypted[-1]
    return decrypted.slice(0, len(decrypted) - pad_len)

# 反序列化存档
static func parse_save(save_data: Dictionary) -> Dictionary:
    var file_head = {}
    var structure_list = {}
    
    # 获取文件头
    for file in save_data:
        if save_data[file].size() > 0:
            file_head[file] = save_data[file][0]
    
    # 获取数据结构
    for file in save_data:
        if file == "gameKey":
            if file_head.get(file) == 3:
                structure_list[file] = GameKey03.new()
            elif file_head.get(file) == 2:
                structure_list[file] = GameKey02.new()
        elif file == "gameProgress":
            if file_head.get(file) == 4:
                structure_list[file] = GameProgress04.new()
            elif file_head.get(file) == 3:
                structure_list[file] = GameProgress03.new()
        elif file == "gameRecord":
            structure_list[file] = GameRecord.new()
        elif file == "settings":
            structure_list[file] = Settings01.new()
        elif file == "user":
            structure_list[file] = User01.new()
    
    # 解密并解析数据
    var parsed_dict = {}
    for file in save_data:
        if save_data[file].size() > 1:
            var encrypted = save_data[file].slice(1)
            var decrypted = decrypt(encrypted)
            var reader = DataType.Reader.new(decrypted)
            parsed_dict[file] = reader.type_read(structure_list[file])
            print("解析文件: " + file)
    
    return parsed_dict

# 序列化存档
static func build_save(parsed_dict: Dictionary) -> Dictionary:
    var file_head = {}
    var structure_list = {}
    var encrypted_dict = {}
    
    # 确定文件头
    for file in parsed_dict:
        if file == "gameKey":
            if parsed_dict[file].has("oldScoreClearedV390"):
                file_head[file] = 3
            else:
                file_head[file] = 2
        elif file == "gameProgress":
            if parsed_dict[file].has("flagOfSongRecordKeyTakumi"):
                file_head[file] = 4
            else:
                file_head[file] = 3
        elif file == "gameRecord":
            file_head[file] = 1
        elif file == "settings":
            file_head[file] = 1
        elif file == "user":
            file_head[file] = 1
    
    # 获取数据结构
    for file in parsed_dict:
        if file == "gameKey":
            if file_head[file] == 3:
                structure_list[file] = GameKey03.new()
            else:
                structure_list[file] = GameKey02.new()
        elif file == "gameProgress":
            if file_head[file] == 4:
                structure_list[file] = GameProgress04.new()
            else:
                structure_list[file] = GameProgress03.new()
        elif file == "gameRecord":
            structure_list[file] = GameRecord.new()
        elif file == "settings":
            structure_list[file] = Settings01.new()
        elif file == "user":
            structure_list[file] = User01.new()
    
    # 加密数据
    for file in parsed_dict:
        var writer = DataType.Writer.new()
        writer.type_write(structure_list[file], parsed_dict[file])
        var data = writer.get_data()
        var encrypted = encrypt(data)
        
        # 添加文件头
        var result = PackedByteArray()
        result.append(file_head[file])
        result.append_array(encrypted)
        encrypted_dict[file] = result
        print("加密文件: " + file)
    
    return encrypted_dict

# 获取存档
static func get_save(input_path: String="user://.save", output_path: String="user://save.json") -> void:
    # 解压存档
    var save_data = unzip_save(input_path)
    if save_data.is_empty():
        return
    
    # 解析存档
    var parsed_dict = parse_save(save_data)
    if parsed_dict.is_empty():
        return
    
    # 保存为JSON
    var save_file = FileAccess.open(output_path, FileAccess.WRITE)
    if save_file:
        save_file.store_string(JSON.stringify(parsed_dict, "\t"))
        save_file.close()
        print("存档已保存至: " + output_path)
    else:
        push_error("无法写入JSON文件: " + output_path)

# 上传存档
static func upload_save(input_path: String="user://save.json", output_path: String="user://.save-1") -> void:
    # 读取JSON存档
    var save_file = FileAccess.open(input_path, FileAccess.READ)
    if not save_file:
        push_error("无法读取JSON文件: " + input_path)
        return
    
    var json_str = save_file.get_as_text()
    save_file.close()
    
    # 解析JSON
    var json = JSON.new()
    var err = json.parse(json_str)
    if err != OK:
        push_error("JSON解析错误: " + json.get_error_message())
        return
    
    var parsed_dict = json.data
    
    # 构建存档
    var encrypted_dict = build_save(parsed_dict)
    
    # 压缩存档
    if zip_save(encrypted_dict, output_path):
        print("存档已上传至: " + output_path)

# 以下是存档结构类
class GameKey03:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        return DataType.GameKey.new().read(data, pos)
    
    func write(data: PackedByteArray, value) -> PackedByteArray:
        return DataType.GameKey.new().write(data, value)

class GameKey02:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        return DataType.GameKey.new().read(data, pos)
    
    func write(data: PackedByteArray, value) -> PackedByteArray:
        return DataType.GameKey.new().write(data, value)

class GameProgress04:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        var reader = DataType.Reader.new(data, pos)
        return [{
            "isFirstRun": reader.type_read(DataType.Bit),
            "legacyChapterFinished": reader.type_read(DataType.Bit),
            "alreadyShowCollectionTip": reader.type_read(DataType.Bit),
            "alreadyShowAutoUnlockINTip": reader.type_read(DataType.Bit),
            "completed": reader.type_read(DataType.StringExt),
            "songUpdateInfo": reader.type_read(DataType.VarInt),
            "challengeModeRank": reader.type_read(DataType.ShortInt),
            "money": reader.type_read(DataType.Money),
            "unlockFlagOfSpasmodic": reader.type_read(DataType.Bits.new(4)),
            "unlockFlagOfIgallta": reader.type_read(DataType.Bits.new(4)),
            "unlockFlagOfRrharil": reader.type_read(DataType.Bits.new(4)),
            "flagOfSongRecordKey": reader.type_read(DataType.Bits),
            "randomVersionUnlocked": reader.type_read(DataType.Bits.new(6)),
            "chapter8UnlockBegin": reader.type_read(DataType.Bit),
            "chapter8UnlockSecondPhase": reader.type_read(DataType.Bit),
            "chapter8Passed": reader.type_read(DataType.Bit),
            "chapter8SongUnlocked": reader.type_read(DataType.Bits.new(6)),
            "flagOfSongRecordKeyTakumi": reader.type_read(DataType.Bits.new(3))
        }, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new(data)
        writer.type_write(DataType.Bit, value["isFirstRun"])
        writer.type_write(DataType.Bit, value["legacyChapterFinished"])
        writer.type_write(DataType.Bit, value["alreadyShowCollectionTip"])
        writer.type_write(DataType.Bit, value["alreadyShowAutoUnlockINTip"])
        writer.type_write(DataType.StringExt, value["completed"])
        writer.type_write(DataType.VarInt, value["songUpdateInfo"])
        writer.type_write(DataType.ShortInt, value["challengeModeRank"])
        writer.type_write(DataType.Money, value["money"])
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfSpasmodic"]))
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfIgallta"]))
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfRrharil"]))
        writer.type_write(DataType.Bits, str(value["flagOfSongRecordKey"]))
        writer.type_write(DataType.Bits.new(6), str(value["randomVersionUnlocked"]))
        writer.type_write(DataType.Bit, value["chapter8UnlockBegin"])
        writer.type_write(DataType.Bit, value["chapter8UnlockSecondPhase"])
        writer.type_write(DataType.Bit, value["chapter8Passed"])
        writer.type_write(DataType.Bits.new(6), str(value["chapter8SongUnlocked"]))
        writer.type_write(DataType.Bits.new(3), str(value["flagOfSongRecordKeyTakumi"]))
        return writer.get_data()

class GameProgress03:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        var reader = DataType.Reader.new(data, pos)
        return [{
            "isFirstRun": reader.type_read(DataType.Bit),
            "legacyChapterFinished": reader.type_read(DataType.Bit),
            "alreadyShowCollectionTip": reader.type_read(DataType.Bit),
            "alreadyShowAutoUnlockINTip": reader.type_read(DataType.Bit),
            "completed": reader.type_read(DataType.StringExt),
            "songUpdateInfo": reader.type_read(DataType.VarInt),
            "challengeModeRank": reader.type_read(DataType.ShortInt),
            "money": reader.type_read(DataType.Money),
            "unlockFlagOfSpasmodic": reader.type_read(DataType.Bits.new(4)),
            "unlockFlagOfIgallta": reader.type_read(DataType.Bits.new(4)),
            "unlockFlagOfRrharil": reader.type_read(DataType.Bits.new(4)),
            "flagOfSongRecordKey": reader.type_read(DataType.Bits),
            "randomVersionUnlocked": reader.type_read(DataType.Bits.new(6)),
            "chapter8UnlockBegin": reader.type_read(DataType.Bit),
            "chapter8UnlockSecondPhase": reader.type_read(DataType.Bit),
            "chapter8Passed": reader.type_read(DataType.Bit),
            "chapter8SongUnlocked": reader.type_read(DataType.Bits.new(6))
        }, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new(data)
        writer.type_write(DataType.Bit, value["isFirstRun"])
        writer.type_write(DataType.Bit, value["legacyChapterFinished"])
        writer.type_write(DataType.Bit, value["alreadyShowCollectionTip"])
        writer.type_write(DataType.Bit, value["alreadyShowAutoUnlockINTip"])
        writer.type_write(DataType.StringExt, value["completed"])
        writer.type_write(DataType.VarInt, value["songUpdateInfo"])
        writer.type_write(DataType.ShortInt, value["challengeModeRank"])
        writer.type_write(DataType.Money, value["money"])
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfSpasmodic"]))
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfIgallta"]))
        writer.type_write(DataType.Bits.new(4), str(value["unlockFlagOfRrharil"]))
        writer.type_write(DataType.Bits, str(value["flagOfSongRecordKey"]))
        writer.type_write(DataType.Bits.new(6), str(value["randomVersionUnlocked"]))
        writer.type_write(DataType.Bit, value["chapter8UnlockBegin"])
        writer.type_write(DataType.Bit, value["chapter8UnlockSecondPhase"])
        writer.type_write(DataType.Bit, value["chapter8Passed"])
        writer.type_write(DataType.Bits.new(6), str(value["chapter8SongUnlocked"]))
        return writer.get_data()

class Settings01:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        var reader = DataType.Reader.new(data, pos)
        return [{
            "chordSupport": reader.type_read(DataType.Bit),
            "fcAPIndicator": reader.type_read(DataType.Bit),
            "enableHitSound": reader.type_read(DataType.Bit),
            "lowResolutionMode": reader.type_read(DataType.Bit),
            "deviceName": reader.type_read(DataType.StringExt),
            "bright": reader.type_read(DataType.Float),
            "musicVolume": reader.type_read(DataType.Float),
            "effectVolume": reader.type_read(DataType.Float),
            "hitSoundVolume": reader.type_read(DataType.Float),
            "soundOffset": reader.type_read(DataType.Float),
            "noteScale": reader.type_read(DataType.Float)
        }, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new(data)
        writer.type_write(DataType.Bit, value["chordSupport"])
        writer.type_write(DataType.Bit, value["fcAPIndicator"])
        writer.type_write(DataType.Bit, value["enableHitSound"])
        writer.type_write(DataType.Bit, value["lowResolutionMode"])
        writer.type_write(DataType.StringExt, value["deviceName"])
        writer.type_write(DataType.Float, value["bright"])
        writer.type_write(DataType.Float, value["musicVolume"])
        writer.type_write(DataType.Float, value["effectVolume"])
        writer.type_write(DataType.Float, value["hitSoundVolume"])
        writer.type_write(DataType.Float, value["soundOffset"])
        writer.type_write(DataType.Float, value["noteScale"])
        return writer.get_data()

class User01:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        var reader = DataType.Reader.new(data, pos)
        return [{
            "showPlayerId": reader.type_read(DataType.Byte),
            "selfIntro": reader.type_read(DataType.StringExt),
            "avatar": reader.type_read(DataType.StringExt),
            "background": reader.type_read(DataType.StringExt)
        }, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new(data)
        writer.type_write(DataType.Byte, value["showPlayerId"])
        writer.type_write(DataType.StringExt, value["selfIntro"])
        writer.type_write(DataType.StringExt, value["avatar"])
        writer.type_write(DataType.StringExt, value["background"])
        return writer.get_data()

class GameRecord:
    extends DataType.dataTypeAbstract
    
    func read(data: PackedByteArray, pos: int) -> Array:
        return DataType.GameRecord.new().read(data, pos)
    
    func write(data: PackedByteArray, value) -> PackedByteArray:
        return DataType.GameRecord.new().write(data, value)
