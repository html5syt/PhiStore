# DataType.gd - Phigros存档数据类型定义
class_name DataType

# 抽象基类
abstract class DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int): pass
    static func write(data: Array, value): pass

# 比特操作类
class Bit:
    static func read(data: int, index: int) -> int:
        return (data >> index) & 1
    
    static func write(data: int, index: int, value: int) -> int:
        var mask = 1 << index
        return (data & ~mask) | ((value & 1) << index)

# 比特位数组(1字节)
class Bits extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var bits = []
        for i in range(8):
            var bit = Bit.read(data[pos], i)
            bits.append(bit)
        return [str(bits), pos + 1]
    
    static func write(data: Array, value: String) -> Array:
        var _value = str_to_var(value)
        if not _value is Array:
            push_error("传入的值不能够被解析为Array！")
            return data
        
        var byte = 0
        if _value.size() < 8:
            for i in range(8 - _value.size()):
                _value.append(0)
        
        for i in range(_value.size()):
            byte = Bit.write(byte, i, _value[i])
        data.append(byte)
        return data

# 带长度限制的比特位
class _Bits extends DataTypeAbstract:
    var _len: int
    
    func _init(length: int = 8):
        _len = length
    
    # 修复：改为静态方法，添加length参数
    static func read(data: PackedByteArray, pos: int, length: int = 8) -> Array:
        var bits = []
        for i in range(length):
            var bit = Bit.read(data[pos], i)
            bits.append(bit)
        return [str(bits), pos + 1]
    
    static func write(data: Array, value: String) -> Array:
        return Bits.write(data, value)

# 创建带长度的Bits类的工厂方法
static func create_bits(length: int) -> _Bits:
    return _Bits.new(length)

# 字节类型
class Byte extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        return [data[pos], pos + 1]
    
    static func write(data: Array, value) -> Array:
        # 处理整数
        if typeof(value) == TYPE_INT:
            data.append(value)
        # 处理浮点数（转换为整数）
        elif typeof(value) == TYPE_FLOAT:
            data.append(int(value))
        # 处理布尔值（转换为0或1）
        elif typeof(value) == TYPE_BOOL:
            data.append(1 if value else 0)
        # 处理数组
        elif value is Array:
            for item in value:
                if typeof(item) == TYPE_INT:
                    data.append(item)
                elif typeof(item) == TYPE_FLOAT:
                    data.append(int(item))
                elif typeof(item) == TYPE_BOOL:
                    data.append(1 if item else 0)
                else:
                    push_error("无法识别的字节数组元素类型: " + str(typeof(item)))
        # 其他类型处理
        else:
            push_error("无法识别的字节值类型: " + str(typeof(value)))
            # 尝试转换为整数
            data.append(int(value))
        
        return data


# 短整型(2字节，小端序)
class ShortInt extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var value = data[pos] | (data[pos + 1] << 8)
        return [value, pos + 2]
    
    static func write(data: Array, value: int) -> Array:
        data.append(value & 0xFF)
        data.append((value >> 8) & 0xFF)
        return data

# 整型(4字节，小端序)
class Int extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24)
        return [value, pos + 4]
    
    static func write(data: Array, value: int) -> Array:
        data.append(value & 0xFF)
        data.append((value >> 8) & 0xFF)
        data.append((value >> 16) & 0xFF)
        data.append((value >> 24) & 0xFF)
        return data

# 浮点型(4字节)
class Float extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var bytes = PackedByteArray([data[pos], data[pos + 1], data[pos + 2], data[pos + 3]])
        var value = bytes.to_float32_array()[0]
        return [value, pos + 4]
    
    static func write(data: Array, value: float) -> Array:
        var bytes = PackedFloat32Array([value]).to_byte_array()
        for i in range(4):
            data.append(bytes[i])
        return data

# 变长整型
class VarInt extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        if data[pos] > 127:
            var var_int = (data[pos] & 0b01111111) ^ (data[pos + 1] << 7)
            return [var_int, pos + 2]
        else:
            return [data[pos], pos + 1]
    
    static func write(data: Array, value: int) -> Array:
        if value > 127:
            data = Byte.write(data, (value & 0b01111111) | 0b10000000)
            data = Byte.write(data, value >> 7)
        else:
            data = Byte.write(data, value)
        return data

# 字符串类型
class StringExt extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var result = VarInt.read(data, pos)
        var string_len = result[0]
        pos = result[1]
        var string_bytes = data.slice(pos, pos + string_len)
        var string_val = string_bytes.get_string_from_utf8()
        return [string_val, pos + string_len]
    
    static func write(data: Array, value: String) -> Array:
        var encoded_string = value.to_utf8_buffer()
        data = VarInt.write(data, encoded_string.size())
        for byte in encoded_string:
            data.append(byte)
        return data

# 游戏Key数据类型
class GameKey extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var all_keys = {}
        var reader = Reader.new(data, pos)
        var key_sum = reader.type_read(VarInt)
        
        for i in range(key_sum):
            var name = reader.type_read(StringExt)
            var length = reader.type_read(Byte)
            var one_key = {}
            all_keys[name] = one_key
            
            # 修复：使用DataType.create_bits()或直接使用_Bits.read()
            var type_bits_result = _Bits.read(data, reader.pos, 5)
            one_key["type"] = type_bits_result[0]
            reader.pos += 1
            
            var flag = []
            for j in range(length - 1):
                var flag_result = Byte.read(data, reader.pos)
                flag.append(flag_result[0])
                reader.pos = flag_result[1]
            one_key["flag"] = str(flag)
        
        return [all_keys, reader.pos]
    
    static func write(data: Array, value: Dictionary) -> Array:
        var writer = Writer.new(data)
        writer.type_write(VarInt, value.size())
        
        for key in value.keys():
            var key_data = value[key]
            writer.type_write(StringExt, key)
            writer.type_write(Byte, str_to_var(key_data["flag"]).size() + 1)
            writer.type_write(Bits, key_data["type"])
            for flag_val in str_to_var(key_data["flag"]):
                writer.type_write(Byte, flag_val)
        
        return writer.get_data()

# 金钱数据类型
class Money extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var money = []
        for i in range(5):
            var result = VarInt.read(data, pos)
            money.append(result[0])
            pos = result[1]
        return [money, pos]
    
    static func write(data: Array, value: Array) -> Array:
        for money_value in value:
            data = VarInt.write(data, money_value)
        return data

# 游戏记录数据类型
class GameRecord extends DataTypeAbstract:
    static func read(data: PackedByteArray, pos: int) -> Array:
        var all_record = {}
        var diff_list = ["EZ", "HD", "IN", "AT", "Legacy"]
        var reader = Reader.new(data, pos)
        var song_sum = reader.type_read(VarInt)
        
        for i in range(song_sum):
            var song_name = reader.type_read(StringExt)
            song_name = song_name.substr(0, song_name.length() - 2) # 移除后两个字符
            var length = reader.type_read(VarInt)
            var end_position = reader.pos + length
            var unlock = reader.type_read(Byte)
            var fc = reader.type_read(Byte)
            var song = {}
            all_record[song_name] = song
            
            for level in range(5):
                if Bit.read(unlock, level):
                    var score = reader.type_read(Int)
                    var acc = reader.type_read(Float)
                    song[diff_list[level]] = {
                        "score": score,
                        "acc": acc,
                        "fc": Bit.read(fc, level)
                    }
            
            if reader.pos != end_position:
                push_warning("在读取\"%s\"的数据时发生错误！当前位置：%d" % [song_name, reader.pos])
                push_warning("错误！！！当前读取字节位置不正确！应为：%d" % end_position)
        
        return [all_record, reader.pos]
    
    static func write(data: Array, value: Dictionary) -> Array:
        var diff_list = {"EZ": 0, "HD": 1, "IN": 2, "AT": 3, "Legacy": 4}
        var writer = Writer.new(data)
        writer.type_write(VarInt, value.size())
        
        for name in value.keys():
            var song = value[name]
            writer.type_write(StringExt, name + ".0")
            writer.type_write(VarInt, song.size() * 8 + 2) # 每个难度8字节(4+4) + unlock(1) + fc(1)
            
            var unlock = str_to_var(Bits.read(PackedByteArray([0]), 0)[0])
            var fc = str_to_var(Bits.read(PackedByteArray([0]), 0)[0])
            var record_writer = Writer.new()
            
            for diff in diff_list.keys():
                if song.has(diff):
                    var index = diff_list[diff]
                    unlock[index] = 1
                    record_writer.type_write(Int, song[diff]["score"])
                    record_writer.type_write(Float, song[diff]["acc"])
                    fc[index] = song[diff]["fc"]
            
            writer.type_write(Bits, str(unlock))
            writer.type_write(Bits, str(fc))
            writer.type_write(Byte, record_writer.get_data())
        
        return writer.get_data()

# 数据读取器
class Reader:
    var data: PackedByteArray
    var pos: int
    var bit_read: Array
    var read_dict: Dictionary
    
    func _init(input_data: PackedByteArray, start_pos: int = 0):
        data = input_data
        pos = start_pos
        bit_read = [PackedByteArray(), false, 0]
        read_dict = {}
    
    func type_read(type_class):
        if type_class == Bit:
            if not bit_read[1]:
                var byte_result = Byte.read(data, pos)
                bit_read[0] = byte_result[0]
                pos = byte_result[1]
                bit_read[1] = true
            var read_data = Bit.read(bit_read[0], bit_read[2])
            bit_read[2] += 1
            return read_data
        else:
            if bit_read[1]:
                bit_read[1] = false
                bit_read[2] = 0
            var result = type_class.read(data, pos)
            pos = result[1]
            return result[0]
    
    func remaining() -> int:
        return data.size() - pos

# 数据写入器
class Writer:
    var data: Array
    var bit_temp: Array
    
    func _init(input_data = null):
        if input_data == null:
            data = []
        elif input_data is Array:
            data = input_data
        else:
            data = []
        bit_temp = [0, false, 0]
    
    func type_write(type_class, value):
        if type_class == Bit:
            if not bit_temp[1]:
                bit_temp[0] = 0
                bit_temp[1] = true
            bit_temp[0] = Bit.write(bit_temp[0], bit_temp[2], value)
            bit_temp[2] += 1
        else:
            if bit_temp[1]:
                bit_temp[1] = false
                bit_temp[2] = 0
                data = Byte.write(data, bit_temp[0])
            data = type_class.write(data, value)
    
    func get_data() -> Array:
        return data
