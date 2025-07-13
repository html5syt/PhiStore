# PackSave.gd - Phigros存档处理主文件
class_name PackSave


# 游戏结构定义
class GameKey03:
    var file_head = PackedByteArray([0x03])
    var keyList: DataType.GameKey
    var lanotaReadKeys: DataType._Bits # 6位
    var camelliaReadKey: DataType.Bits
    var sideStory4BeginReadKey: DataType.Byte
    var oldScoreClearedV390: DataType.Byte
    
    # 解析GameKey03结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["keyList"] = reader.type_read(DataType.GameKey)
        result["lanotaReadKeys"] = reader.type_read(DataType.create_bits(6))
        result["camelliaReadKey"] = reader.type_read(DataType.Bits)
        result["sideStory4BeginReadKey"] = reader.type_read(DataType.Byte)
        result["oldScoreClearedV390"] = reader.type_read(DataType.Byte)
        return result
    
    # 构建GameKey03结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        writer.type_write(DataType.GameKey, data["keyList"])
        writer.type_write(DataType.create_bits(6), data["lanotaReadKeys"])
        writer.type_write(DataType.Bits, data["camelliaReadKey"])
        
        # 确保整数值类型正确
        var sideStory = int(data["sideStory4BeginReadKey"])
        var oldScore = int(data["oldScoreClearedV390"])
        
        writer.type_write(DataType.Byte, sideStory)
        writer.type_write(DataType.Byte, oldScore)
        
        return PackedByteArray(writer.get_data())

class GameKey02:
    var file_head = PackedByteArray([0x02])
    var keyList: DataType.GameKey
    var lanotaReadKeys: DataType._Bits # 6位
    var camelliaReadKey: DataType.Bits
    
    # 解析GameKey02结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["keyList"] = reader.type_read(DataType.GameKey)
        result["lanotaReadKeys"] = reader.type_read(DataType.create_bits(6))
        result["camelliaReadKey"] = reader.type_read(DataType.Bits)
        return result
    
    # 构建GameKey02结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        writer.type_write(DataType.GameKey, data["keyList"])
        writer.type_write(DataType.create_bits(6), data["lanotaReadKeys"])
        writer.type_write(DataType.Bits, data["camelliaReadKey"])
        return PackedByteArray(writer.get_data())

class GameProgress04:
    var file_head = PackedByteArray([0x04])
    var isFirstRun: DataType.Bit
    var legacyChapterFinished: DataType.Bit
    var alreadyShowCollectionTip: DataType.Bit
    var alreadyShowAutoUnlockINTip: DataType.Bit
    var completed: DataType.StringExt
    var songUpdateInfo: DataType.VarInt
    var challengeModeRank: DataType.ShortInt
    var money: DataType.Money
    var unlockFlagOfSpasmodic: DataType._Bits # 4位
    var unlockFlagOfIgallta: DataType._Bits # 4位
    var unlockFlagOfRrharil: DataType._Bits # 4位
    var flagOfSongRecordKey: DataType.Bits
    var randomVersionUnlocked: DataType._Bits # 6位
    var chapter8UnlockBegin: DataType.Bit
    var chapter8UnlockSecondPhase: DataType.Bit
    var chapter8Passed: DataType.Bit
    var chapter8SongUnlocked: DataType._Bits # 6位
    var flagOfSongRecordKeyTakumi: DataType._Bits # 3位
    
    # 解析GameProgress04结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["isFirstRun"] = reader.type_read(DataType.Bit)
        result["legacyChapterFinished"] = reader.type_read(DataType.Bit)
        result["alreadyShowCollectionTip"] = reader.type_read(DataType.Bit)
        result["alreadyShowAutoUnlockINTip"] = reader.type_read(DataType.Bit)
        result["completed"] = reader.type_read(DataType.StringExt)
        result["songUpdateInfo"] = reader.type_read(DataType.VarInt)
        result["challengeModeRank"] = reader.type_read(DataType.ShortInt)
        result["money"] = reader.type_read(DataType.Money)
        result["unlockFlagOfSpasmodic"] = reader.type_read(DataType.create_bits(4))
        result["unlockFlagOfIgallta"] = reader.type_read(DataType.create_bits(4))
        result["unlockFlagOfRrharil"] = reader.type_read(DataType.create_bits(4))
        result["flagOfSongRecordKey"] = reader.type_read(DataType.Bits)
        result["randomVersionUnlocked"] = reader.type_read(DataType.create_bits(6))
        result["chapter8UnlockBegin"] = reader.type_read(DataType.Bit)
        result["chapter8UnlockSecondPhase"] = reader.type_read(DataType.Bit)
        result["chapter8Passed"] = reader.type_read(DataType.Bit)
        result["chapter8SongUnlocked"] = reader.type_read(DataType.create_bits(6))
        result["flagOfSongRecordKeyTakumi"] = reader.type_read(DataType.create_bits(3))
        return result
    
    # 构建GameProgress04结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        # writer.type_write(DataType.Bit, data["isFirstRun"])
        # writer.type_write(DataType.Bit, data["legacyChapterFinished"])
        # writer.type_write(DataType.Bit, data["alreadyShowCollectionTip"])
        # writer.type_write(DataType.Bit, data["alreadyShowAutoUnlockINTip"])
    # 确保所有位值都是整数
        writer.type_write(DataType.Bit, int(data["isFirstRun"]))
        writer.type_write(DataType.Bit, int(data["legacyChapterFinished"]))
        writer.type_write(DataType.Bit, int(data["alreadyShowCollectionTip"]))
        writer.type_write(DataType.Bit, int(data["alreadyShowAutoUnlockINTip"]))
        writer.type_write(DataType.StringExt, data["completed"])
        writer.type_write(DataType.VarInt, data["songUpdateInfo"])
        writer.type_write(DataType.ShortInt, data["challengeModeRank"])
        writer.type_write(DataType.Money, data["money"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfSpasmodic"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfIgallta"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfRrharil"])
        writer.type_write(DataType.Bits, data["flagOfSongRecordKey"])
        writer.type_write(DataType.create_bits(6), data["randomVersionUnlocked"])
        writer.type_write(DataType.Bit, int(data["chapter8UnlockBegin"]))
        writer.type_write(DataType.Bit, int(data["chapter8UnlockSecondPhase"]))
        writer.type_write(DataType.Bit, int(data["chapter8Passed"]))
        writer.type_write(DataType.create_bits(6), data["chapter8SongUnlocked"])
        writer.type_write(DataType.create_bits(3), data["flagOfSongRecordKeyTakumi"])
        return PackedByteArray(writer.get_data())

class GameProgress03:
    var file_head = PackedByteArray([0x03])
    # 其他字段与GameProgress04相同，除了flagOfSongRecordKeyTakumi
    
    # 解析GameProgress03结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["isFirstRun"] = reader.type_read(DataType.Bit)
        result["legacyChapterFinished"] = reader.type_read(DataType.Bit)
        result["alreadyShowCollectionTip"] = reader.type_read(DataType.Bit)
        result["alreadyShowAutoUnlockINTip"] = reader.type_read(DataType.Bit)
        result["completed"] = reader.type_read(DataType.StringExt)
        result["songUpdateInfo"] = reader.type_read(DataType.VarInt)
        result["challengeModeRank"] = reader.type_read(DataType.ShortInt)
        result["money"] = reader.type_read(DataType.Money)
        result["unlockFlagOfSpasmodic"] = reader.type_read(DataType.create_bits(4))
        result["unlockFlagOfIgallta"] = reader.type_read(DataType.create_bits(4))
        result["unlockFlagOfRrharil"] = reader.type_read(DataType.create_bits(4))
        result["flagOfSongRecordKey"] = reader.type_read(DataType.Bits)
        result["randomVersionUnlocked"] = reader.type_read(DataType.create_bits(6))
        result["chapter8UnlockBegin"] = reader.type_read(DataType.Bit)
        result["chapter8UnlockSecondPhase"] = reader.type_read(DataType.Bit)
        result["chapter8Passed"] = reader.type_read(DataType.Bit)
        result["chapter8SongUnlocked"] = reader.type_read(DataType.create_bits(6))
        return result
    
    # 构建GameProgress03结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        writer.type_write(DataType.Bit, data["isFirstRun"])
        writer.type_write(DataType.Bit, data["legacyChapterFinished"])
        writer.type_write(DataType.Bit, data["alreadyShowCollectionTip"])
        writer.type_write(DataType.Bit, data["alreadyShowAutoUnlockINTip"])
        writer.type_write(DataType.StringExt, data["completed"])
        writer.type_write(DataType.VarInt, data["songUpdateInfo"])
        writer.type_write(DataType.ShortInt, data["challengeModeRank"])
        writer.type_write(DataType.Money, data["money"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfSpasmodic"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfIgallta"])
        writer.type_write(DataType.create_bits(4), data["unlockFlagOfRrharil"])
        writer.type_write(DataType.Bits, data["flagOfSongRecordKey"])
        writer.type_write(DataType.create_bits(6), data["randomVersionUnlocked"])
        writer.type_write(DataType.Bit, data["chapter8UnlockBegin"])
        writer.type_write(DataType.Bit, data["chapter8UnlockSecondPhase"])
        writer.type_write(DataType.Bit, data["chapter8Passed"])
        writer.type_write(DataType.create_bits(6), data["chapter8SongUnlocked"])
        return PackedByteArray(writer.get_data())

class Settings01:
    var file_head = PackedByteArray([0x01])
    var chordSupport: DataType.Bit
    var fcAPIndicator: DataType.Bit
    var enableHitSound: DataType.Bit
    var lowResolutionMode: DataType.Bit
    var deviceName: DataType.StringExt
    var bright: DataType.Float
    var musicVolume: DataType.Float
    var effectVolume: DataType.Float
    var hitSoundVolume: DataType.Float
    var soundOffset: DataType.Float
    var noteScale: DataType.Float
    
    # 解析Settings01结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["chordSupport"] = reader.type_read(DataType.Bit)
        result["fcAPIndicator"] = reader.type_read(DataType.Bit)
        result["enableHitSound"] = reader.type_read(DataType.Bit)
        result["lowResolutionMode"] = reader.type_read(DataType.Bit)
        result["deviceName"] = reader.type_read(DataType.StringExt)
        result["bright"] = reader.type_read(DataType.Float)
        result["musicVolume"] = reader.type_read(DataType.Float)
        result["effectVolume"] = reader.type_read(DataType.Float)
        result["hitSoundVolume"] = reader.type_read(DataType.Float)
        result["soundOffset"] = reader.type_read(DataType.Float)
        result["noteScale"] = reader.type_read(DataType.Float)
        return result
    
    # 构建Settings01结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        writer.type_write(DataType.Bit, int(data["chordSupport"]))
        writer.type_write(DataType.Bit, int(data["fcAPIndicator"]))
        writer.type_write(DataType.Bit, int(data["enableHitSound"]))
        writer.type_write(DataType.Bit, int(data["lowResolutionMode"]))
        writer.type_write(DataType.StringExt, data["deviceName"])
        writer.type_write(DataType.Float, data["bright"])
        writer.type_write(DataType.Float, data["musicVolume"])
        writer.type_write(DataType.Float, data["effectVolume"])
        writer.type_write(DataType.Float, data["hitSoundVolume"])
        writer.type_write(DataType.Float, data["soundOffset"])
        writer.type_write(DataType.Float, data["noteScale"])
        return PackedByteArray(writer.get_data())

class User01:
    var file_head = PackedByteArray([0x01])
    var showPlayerId: DataType.Byte
    var selfIntro: DataType.StringExt
    var avatar: DataType.StringExt
    var background: DataType.StringExt
    
    # 解析User01结构
    static func parse(data: PackedByteArray) -> Dictionary:
        var reader = DataType.Reader.new(data, 0)
        var result = {}
        result["showPlayerId"] = reader.type_read(DataType.Byte)
        result["selfIntro"] = reader.type_read(DataType.StringExt)
        result["avatar"] = reader.type_read(DataType.StringExt)
        result["background"] = reader.type_read(DataType.StringExt)
        return result
    
    # 构建User01结构
    static func build(data: Dictionary) -> PackedByteArray:
        var writer = DataType.Writer.new()
        writer.type_write(DataType.Byte, data["showPlayerId"])
        writer.type_write(DataType.StringExt, data["selfIntro"])
        writer.type_write(DataType.StringExt, data["avatar"])
        writer.type_write(DataType.StringExt, data["background"])
        return PackedByteArray(writer.get_data())

# AES加密相关
const AES_KEY = "6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks="
const AES_IV = "Kk/wisgNYwcAV8WVGMgyUw=="

# 存档文件列表
const SAVE_LIST = [
    "gameKey",
    "gameProgress",
    "gameRecord",
    "settings",
    "user"
]

# AES加密
static func encrypt(data: PackedByteArray) -> PackedByteArray:
    var aes = AESContext.new()
    var key = Marshalls.base64_to_raw(AES_KEY)
    var iv = Marshalls.base64_to_raw(AES_IV)
    
    # 填充数据到16字节的倍数
    var padded_data = data.duplicate()
    var padding_size = 16 - (data.size() % 16)
    if padding_size != 16:
        for i in range(padding_size):
            padded_data.append(padding_size)
    
    aes.start(AESContext.MODE_CBC_ENCRYPT, key, iv)
    var encrypted = aes.update(padded_data)
    aes.finish()
    return encrypted

# AES解密
static func decrypt(data: PackedByteArray) -> PackedByteArray:
    var aes = AESContext.new()
    var key = Marshalls.base64_to_raw(AES_KEY)
    var iv = Marshalls.base64_to_raw(AES_IV)
    
    aes.start(AESContext.MODE_CBC_DECRYPT, key, iv)
    var decrypted = aes.update(data)
    aes.finish()
    
    # 移除填充
    if decrypted.size() > 0:
        var padding_size = decrypted[decrypted.size() - 1]
        if padding_size <= 16:
            decrypted = decrypted.slice(0, decrypted.size() - padding_size)
    
    return decrypted

# 解压存档
static func unzip_save(zip_path: String) -> Dictionary:
    var save_dict = {}
    var zip_reader = ZIPReader.new()
    
    if zip_reader.open(zip_path) != OK:
        push_error("无法打开存档文件: " + zip_path)
        return save_dict
    
    var files = zip_reader.get_files()
    for filename in files:
        print("解压 \"%s\" 文件" % filename)
        var file_data = zip_reader.read_file(filename)
        if file_data.size() > 0:
            save_dict[filename] = file_data
        else:
            push_warning("文件 \"%s\" 为空或读取失败" % filename)
    
    zip_reader.close()
    print("解压完毕！")
    return save_dict

# 压缩存档
static func zip_save(save_dict: Dictionary, output_path: String) -> bool:
    var zip_packer = ZIPPacker.new()
    
    if zip_packer.open(output_path) != OK:
        push_error("无法创建存档文件: " + output_path)
        return false
    
    for filename in save_dict.keys():
        print("压缩 \"%s\" 文件" % filename)
        var file_data = save_dict[filename]
        if file_data is PackedByteArray:
            zip_packer.start_file(filename)
            zip_packer.write_file(file_data)
            zip_packer.close_file()
        else:
            push_warning("跳过无效文件数据: " + filename)
    
    zip_packer.close()
    print("压缩完毕！")
    return true

# 获取结构定义
static func get_structure(file_head: Dictionary) -> Dictionary:
    var structure_list = {}
    
    # gameKey
    if file_head.has("gameKey"):
        var head = file_head["gameKey"]
        if head.size() > 0:
            if head[0] == 0x03:
                structure_list["gameKey"] = GameKey03
            elif head[0] == 0x02:
                structure_list["gameKey"] = GameKey02
            else:
                push_error("gameKey文件头不正确: %02x" % head[0])
    
    # gameProgress
    if file_head.has("gameProgress"):
        var head = file_head["gameProgress"]
        if head.size() > 0:
            if head[0] == 0x04:
                structure_list["gameProgress"] = GameProgress04
            elif head[0] == 0x03:
                structure_list["gameProgress"] = GameProgress03
            else:
                push_error("gameProgress文件头不正确: %02x" % head[0])
    
    # gameRecord
    if file_head.has("gameRecord"):
        var head = file_head["gameRecord"]
        if head.size() > 0 and head[0] == 0x01:
            structure_list["gameRecord"] = DataType.GameRecord
        else:
            push_error("gameRecord文件头不正确")
    
    # settings
    if file_head.has("settings"):
        var head = file_head["settings"]
        if head.size() > 0 and head[0] == 0x01:
            structure_list["settings"] = Settings01
        else:
            push_error("settings文件头不正确")
    
    # user
    if file_head.has("user"):
        var head = file_head["user"]
        if head.size() > 0 and head[0] == 0x01:
            structure_list["user"] = User01
        else:
            push_error("user文件头不正确")
    
    return structure_list

# 获取文件头
static func get_file_head(save_dict: Dictionary) -> Dictionary:
    var file_head = {}
    
    for key in save_dict.keys():
        var file_dict = save_dict[key]
        if key == "gameKey":
            if file_dict.has("oldScoreClearedV390"):
                file_head[key] = PackedByteArray([0x03])
            else:
                file_head[key] = PackedByteArray([0x02])
        elif key == "gameRecord":
            file_head[key] = PackedByteArray([0x01])
        elif key == "gameProgress":
            if file_dict.has("flagOfSongRecordKeyTakumi"):
                file_head[key] = PackedByteArray([0x04])
            else:
                file_head[key] = PackedByteArray([0x03])
        elif key == "settings":
            file_head[key] = PackedByteArray([0x01])
        elif key == "user":
            file_head[key] = PackedByteArray([0x01])
    
    return file_head

# 解析结构数据
static func parse_structure_data(structure_class, data: PackedByteArray) -> Dictionary:
    # 根据结构类调用对应的解析方法
    if structure_class == DataType.GameRecord:
        var result = DataType.GameRecord.read(data, 0)
        return result[0]
    elif structure_class == GameKey03:
        return GameKey03.parse(data)
    elif structure_class == GameKey02:
        return GameKey02.parse(data)
    elif structure_class == GameProgress04:
        return GameProgress04.parse(data)
    elif structure_class == GameProgress03:
        return GameProgress03.parse(data)
    elif structure_class == Settings01:
        return Settings01.parse(data)
    elif structure_class == User01:
        return User01.parse(data)
    else:
        push_warning("结构解析尚未完全实现: " + str(structure_class))
        return {}

# 构建结构数据
static func build_structure_data(structure_class, data: Dictionary) -> PackedByteArray:
    # 根据结构类调用对应的构建方法
    if structure_class == DataType.GameRecord:
        var writer_data = []
        DataType.GameRecord.write(writer_data, data)
        var result = PackedByteArray()
        for byte in writer_data:
            result.append(byte)
        return result
    elif structure_class == GameKey03:
        return GameKey03.build(data)
    elif structure_class == GameKey02:
        return GameKey02.build(data)
    elif structure_class == GameProgress04:
        return GameProgress04.build(data)
    elif structure_class == GameProgress03:
        return GameProgress03.build(data)
    elif structure_class == Settings01:
        return Settings01.build(data)
    elif structure_class == User01:
        return User01.build(data)
    else:
        push_warning("结构构建尚未完全实现: " + str(structure_class))
        return PackedByteArray()

# 解密存档数据
static func decrypt_save(save_dict: Dictionary) -> Dictionary:
    var file_head = {}
    for key in save_dict.keys():
        var value = save_dict[key]
        if value.size() > 0:
            file_head[key] = value.slice(0, 1)
    
    var structure_list = get_structure(file_head)
    var result_dict = {}
    
    for key in save_dict.keys():
        var value = save_dict[key]
        if value.size() > 1:
            var decrypted_data = decrypt(value.slice(1))
            if structure_list.has(key):
                result_dict[key] = parse_structure_data(structure_list[key], decrypted_data)
            else:
                push_warning("未找到结构定义: " + key)
                result_dict[key] = {}
        else:
            push_warning("文件数据太短: " + key)
            result_dict[key] = {}
    
    return result_dict

# 加密存档数据
static func encrypt_save(save_dict: Dictionary) -> Dictionary:
    var file_head = get_file_head(save_dict)
    var structure_list = get_structure(file_head)
    var result_dict = {}
    
    for key in save_dict.keys():
        var value = save_dict[key]
        if structure_list.has(key) and file_head.has(key):
            var built_data = build_structure_data(structure_list[key], value)
            var encrypted_data = encrypt(built_data)
            var final_data = file_head[key].duplicate()
            final_data.append_array(encrypted_data)
            result_dict[key] = final_data
        else:
            push_warning("跳过未知结构: " + key)
    
    return result_dict

# 解析存档（完整流程）
static func parse_save(save_path: String) -> Dictionary:
    var save_dict = unzip_save(save_path)
    return decrypt_save(save_dict)

# 构建存档（完整流程）
static func build_save(save_dict: Dictionary, output_path: String) -> bool:
    var encrypted_dict = encrypt_save(save_dict)
    return zip_save(encrypted_dict, output_path)

# 获取存档（对外接口）
static func dePack(source: String = "user://.save", output: String = "user://PhigrosSaves.json"):
    if not FileAccess.file_exists(source):
        push_error("存档文件不存在: " + source)
        return
    
    var save_dict = parse_save(source)
    
    # 写出JSON文件
    var file = FileAccess.open(output, FileAccess.WRITE)
    if file:
        file.store_string(JSON.stringify(save_dict, "\t" if OS.has_feature("debug") else null))
        file.close()
        print("序列化存档成功！")
    else:
        push_error("无法写入JSON文件: " + output)

# 上传存档（对外接口）
static func Pack(source: String = "user://PhigrosSaves.json", output: String = "user://.save"):
    if not FileAccess.file_exists(source):
        push_error("JSON文件不存在: " + source)
        return
    
    var file = FileAccess.open(source, FileAccess.READ)
    if not file:
        push_error("无法读取JSON文件: " + source)
        return
    
    var json_text = file.get_as_text()
    file.close()
    
    var json = JSON.new()
    var parse_result = json.parse(json_text)
    if parse_result != OK:
        push_error("JSON解析失败: " + json.get_error_message())
        return
    
    var save_dict = json.get_data()
    if build_save(save_dict, output):
        print("反序列化存档成功！")
    else:
        push_error("构建存档失败")
