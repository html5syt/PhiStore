using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

public static class DataType
{
    public static int ReadBit(int data, int index)
    {
        return (data >> index) & 1;
    }

    public static int WriteBit(int data, int index, int value)
    {
        int mask = 1 << index;
        return (data & ~mask) | ((value & 1) << index);
    }
}

public interface IDataType
{
    object Read(byte[] data, ref int pos);
    void Write(List<byte> data, object value);
}

public class Bits : IDataType
{
    private readonly int _length;

    public Bits(int length = 8)
    {
        _length = length;
    }

    public object Read(byte[] data, ref int pos)
    {
        List<int> bits = new List<int>();
        for (int i = 0; i < _length; i++)
        {
            bits.Add(DataType.ReadBit(data[pos], i));
        }
        return bits;
    }

    public void Write(List<byte> data, object value)
    {
        List<int> bits = (List<int>)value;
        int byteValue = 0;
        for (int i = 0; i < Math.Min(8, bits.Count); i++)
        {
            byteValue = DataType.WriteBit(byteValue, i, bits[i]);
        }
        data.Add((byte)byteValue);
    }
}

public class Byte : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        return data[pos++];
    }

    public void Write(List<byte> data, object value)
    {
        if (value is int intValue)
        {
            data.Add((byte)intValue);
        }
        else if (value is IEnumerable<byte> bytes)
        {
            data.AddRange(bytes);
        }
    }
}

public class ShortInt : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        short result = BitConverter.ToInt16(data, pos);
        pos += 2;
        return result;
    }

    public void Write(List<byte> data, object value)
    {
        data.AddRange(BitConverter.GetBytes((short)value));
    }
}

public class Int : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        int result = BitConverter.ToInt32(data, pos);
        pos += 4;
        return result;
    }

    public void Write(List<byte> data, object value)
    {
        data.AddRange(BitConverter.GetBytes((int)value));
    }
}

public class Float : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        float result = BitConverter.ToSingle(data, pos);
        pos += 4;
        return result;
    }

    public void Write(List<byte> data, object value)
    {
        data.AddRange(BitConverter.GetBytes((float)value));
    }
}

public class VarInt : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        if (data[pos] > 127)
        {
            int value = (data[pos] & 0x7F) | (data[pos + 1] << 7);
            pos += 2;
            return value;
        }
        return (int)data[pos++];
    }

    public void Write(List<byte> data, object value)
    {
        int intValue = (int)value;
        if (intValue > 127)
        {
            data.Add((byte)((intValue & 0x7F) | 0x80));
            data.Add((byte)(intValue >> 7));
        }
        else
        {
            data.Add((byte)intValue);
        }
    }
}

public class String : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        int length = (int)new VarInt().Read(data, ref pos);
        string result = Encoding.UTF8.GetString(data, pos, length);
        pos += length;
        return result;
    }

    public void Write(List<byte> data, object value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes((string)value);
        new VarInt().Write(data, bytes.Length);
        data.AddRange(bytes);
    }
}

public class Reader
{
    private byte[] _data;
    private int _pos;
    private byte _bitByte;
    private int _bitIndex;
    private bool _inBitMode;
    
    public int Position => _pos;

    public Reader(byte[] data, int pos = 0)
    {
        _data = data;
        _pos = pos;
    }

    public object Read(IDataType type)
    {
        if (type is Bit)
        {
            if (!_inBitMode)
            {
                _bitByte = (byte)new Byte().Read(_data, ref _pos);
                _inBitMode = true;
                _bitIndex = 0;
            }
            int bit = DataType.ReadBit(_bitByte, _bitIndex++);
            if (_bitIndex >= 8)
            {
                _inBitMode = false;
            }
            return bit;
        }
        
        if (_inBitMode)
        {
            _inBitMode = false;
        }
        
        return type.Read(_data, ref _pos);
    }

    public int Remaining()
    {
        return _data.Length - _pos;
    }
}

public class Writer
{
    private List<byte> _data = new List<byte>();
    private byte _bitByte;
    private int _bitIndex;
    private bool _inBitMode;
    
    public int Position => _data.Count;

    public void Write(IDataType type, object value)
    {
        if (type is Bit)
        {
            if (!_inBitMode)
            {
                _bitByte = 0;
                _inBitMode = true;
                _bitIndex = 0;
            }
            _bitByte = (byte)DataType.WriteBit(_bitByte, _bitIndex++, (int)value);
            if (_bitIndex >= 8)
            {
                new Byte().Write(_data, _bitByte);
                _inBitMode = false;
            }
            return;
        }

        if (_inBitMode)
        {
            new Byte().Write(_data, _bitByte);
            _inBitMode = false;
        }
        
        type.Write(_data, value);
    }

    public byte[] GetData()
    {
        if (_inBitMode)
        {
            new Byte().Write(_data, _bitByte);
        }
        return _data.ToArray();
    }
}

public class Bit : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        return DataType.ReadBit(data[pos], 0);
    }

    public void Write(List<byte> data, object value)
    {
        throw new NotImplementedException("Use Writer class for bit-level writing");
    }
}