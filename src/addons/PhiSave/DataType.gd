extends RefCounted

class_name DataType

# 数据类型基类
class dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array: # 返回 [value, new_pos]
        return [null, pos]
    
    func write(data: PackedByteArray, value) -> PackedByteArray:
        return data

# 位操作类
class Bit:
    static func read(data: int, index: int) -> int:
        return (data >> index) & 1
    
    static func write(data: int, index: int, value: int) -> int:
        var mask = 1 << index
        return (data & ~mask) | ((value & 1) << index)

# 位集合类
class Bits extends dataTypeAbstract:
    var _len: int = 8
    
    func _init(length: int = 8):
        _len = length
    
    func read(data: PackedByteArray, pos: int) -> Array:
        var bits: Array = []
        for i in range(_len):
            bits.append(Bit.read(data[pos], i))
        return [bits, pos + 1]
    
    func write(data: PackedByteArray, value: Array) -> PackedByteArray:
        var byte = 0
        if len(value) < 8:
            for i in range(8 - len(value)):
                value.append(0)
        
        for i in range(len(value)):
            byte = Bit.write(byte, i, value[i])
        
        data.append(byte)
        return data

# 字节类
class Byte extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        return [data[pos], pos + 1]
    
    func write(data: PackedByteArray, value) -> PackedByteArray:
        if typeof(value) == TYPE_INT:
            data.append(value)
        else:
            data.append_array(value)
        return data

# 短整型类
class ShortInt extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var buffer = StreamPeerBuffer.new()
        buffer.data_array = data.slice(pos, pos + 2)
        return [buffer.get_u16(), pos + 2]
    
    func write(data: PackedByteArray, value: int) -> PackedByteArray:
        var buffer = StreamPeerBuffer.new()
        buffer.put_u16(value)
        data.append_array(buffer.data_array)
        return data

# 整型类
class Int extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var buffer = StreamPeerBuffer.new()
        buffer.data_array = data.slice(pos, pos + 4)
        return [buffer.get_u32(), pos + 4]
    
    func write(data: PackedByteArray, value: int) -> PackedByteArray:
        var buffer = StreamPeerBuffer.new()
        buffer.put_u32(value)
        data.append_array(buffer.data_array)
        return data

# 浮点型类
class Float extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var buffer = StreamPeerBuffer.new()
        buffer.data_array = data.slice(pos, pos + 4)
        return [buffer.get_float(), pos + 4]
    
    func write(data: PackedByteArray, value: float) -> PackedByteArray:
        var buffer = StreamPeerBuffer.new()
        buffer.put_float(value)
        data.append_array(buffer.data_array)
        return data

# 变长整型类
class VarInt extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        if data[pos] > 127:
            var var_int = (data[pos] & 0b01111111) | (data[pos + 1] << 7)
            return [var_int, pos + 2]
        else:
            return [data[pos], pos + 1]
    
    func write(data: PackedByteArray, value: int) -> PackedByteArray:
        if value > 127:
            data.append((value & 0b01111111) | 0b10000000)
            data.append(value >> 7)
        else:
            data.append(value)
        return data

# 字符串类
class StringExt extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var varint = VarInt.new()
        var result = varint.read(data, pos)
        var string_len = result[0]
        pos = result[1]
        
        var string_val = data.slice(pos, pos + string_len).get_string_from_utf8()
        return [string_val, pos + string_len]
    
    func write(data: PackedByteArray, value: String) -> PackedByteArray:
        var encoded = value.to_utf8_buffer()
        var varint = VarInt.new()
        data = varint.write(data, len(encoded))
        data.append_array(encoded)
        return data

# 游戏键类
class GameKey extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var all_keys = {}
        var reader = Reader.new(data, pos)
        var key_sum = reader.type_read(VarInt.new())
        
        for _i in range(key_sum):
            var name = reader.type_read(StringExt.new())
            var length = reader.type_read(Byte.new())
            var one_key = {}
            one_key["type"] = reader.type_read(Bits.new(5))
            
            var flags = []
            for _j in range(length - 1):
                flags.append(reader.type_read(Byte.new()))
            one_key["flag"] = flags
            all_keys[name] = one_key
        
        return [all_keys, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var writer = Writer.new(data)
        writer.type_write(VarInt.new(), len(value))
        
        for key in value:
            writer.type_write(StringExt.new(), key)
            var flags = value[key]["flag"]
            writer.type_write(Byte.new(), len(flags) + 1)
            writer.type_write(Bits.new(), value[key]["type"])
            for flag in flags:
                writer.type_write(Byte.new(), flag)
        
        return writer.get_data()

# 货币类
class Money extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var money = []
        var varint = VarInt.new()
        
        for _i in range(5):
            var result = varint.read(data, pos)
            money.append(result[0])
            pos = result[1]
        
        return [money, pos]
    
    func write(data: PackedByteArray, value: Array) -> PackedByteArray:
        var varint = VarInt.new()
        for money_value in value:
            data = varint.write(data, money_value)
        return data

# 游戏记录类
class GameRecord extends dataTypeAbstract:
    func read(data: PackedByteArray, pos: int) -> Array:
        var all_record = {}
        var diff_list = ["EZ", "HD", "IN", "AT", "Legacy"]
        var reader = Reader.new(data, pos)
        var song_sum = reader.type_read(VarInt.new())
        
        for _i in range(song_sum):
            var song_name = reader.type_read(StringExt.new())
            if song_name.ends_with(".0"):
                song_name = song_name.substr(0, song_name.length() - 2)
            
            var length = reader.type_read(VarInt.new())
            var end_pos = reader.pos + length
            
            var unlock = reader.type_read(Byte.new())
            var fc = reader.type_read(Byte.new())
            var song = {}
            
            for level in range(5):
                if Bit.read(unlock, level) == 1:
                    var score = reader.type_read(Int.new())
                    var acc = reader.type_read(Float.new())
                    song[diff_list[level]] = {
                        "score": score,
                        "acc": acc,
                        "fc": Bit.read(fc, level)
                    }
            
            if reader.pos != end_pos:
                push_warning("读取错误: %s 位置: %d 应为: %d" % [song_name, reader.pos, end_pos])
            all_record[song_name] = song
        
        return [all_record, reader.pos]
    
    func write(data: PackedByteArray, value: Dictionary) -> PackedByteArray:
        var diff_list = {"EZ": 0, "HD": 1, "IN": 2, "AT": 3, "Legacy": 4}
        var writer = Writer.new(data)
        writer.type_write(VarInt.new(), len(value))
        
        for name in value:
            writer.type_write(StringExt.new(), name + ".0")
            var song = value[name]
            var record_writer = Writer.new()
            
            var unlock = [0, 0, 0, 0, 0]
            var fc = [0, 0, 0, 0, 0]
            var count = 0
            
            for diff in diff_list:
                var index = diff_list[diff]
                if song.has(diff):
                    unlock[index] = 1
                    record_writer.type_write(Int.new(), song[diff]["score"])
                    record_writer.type_write(Float.new(), song[diff]["acc"])
                    fc[index] = song[diff]["fc"]
                    count += 8  # 每个记录占8字节 (Int + Float)
            
            # 写入解锁和FC信息
            writer.type_write(VarInt.new(), count + 2)  # +2 用于解锁和FC字节
            writer.type_write(Bits.new(), unlock)
            writer.type_write(Bits.new(), fc)
            writer.get_data().append_array(record_writer.get_data())
        
        return writer.get_data()

# 读取器类
class Reader:
    var data: PackedByteArray
    var pos: int
    var bit_read: Array = [0, false, 0]  # [current_byte, is_active, bit_index]
    
    func _init(data_arr: PackedByteArray, start_pos: int = 0):
        data = data_arr
        pos = start_pos
    
    func type_read(type_class) -> Variant:
        if type_class is Bit:
            if not bit_read[1]:
                var result = Byte.new().read(data, pos)
                bit_read[0] = result[0]
                pos = result[1]
                bit_read[1] = true
            
            var bit_value = Bit.read(bit_read[0], bit_read[2])
            bit_read[2] += 1
            return bit_value
        else:
            if bit_read[1]:
                bit_read[1] = false
                bit_read[2] = 0
            
            var result = type_class.read(data, pos)
            pos = result[1]
            return result[0]
    
    func remaining() -> int:
        return len(data) - pos

# 写入器类
class Writer:
    var data: PackedByteArray = PackedByteArray()
    var bit_temp: Array = [0, false, 0]  # [current_byte, is_active, bit_index]
    
    func _init(initial_data: PackedByteArray = PackedByteArray()):
        if initial_data.size() > 0:
            data = initial_data
    
    func type_write(type_class, value) -> void:
        if type_class is Bit:
            if not bit_temp[1]:
                bit_temp[0] = 0
                bit_temp[1] = true
            bit_temp[0] = Bit.write(bit_temp[0], bit_temp[2], value)
            bit_temp[2] += 1
        else:
            if bit_temp[1]:
                data = Byte.new().write(data, bit_temp[0])
                bit_temp[1] = false
                bit_temp[2] = 0
            data = type_class.write(data, value)
    
    func get_data() -> PackedByteArray:
        if bit_temp[1]:
            data = Byte.new().write(data, bit_temp[0])
            bit_temp[1] = false
            bit_temp[2] = 0
        return data
