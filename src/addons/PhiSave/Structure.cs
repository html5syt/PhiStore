using System;
using System.Collections.Generic;
using System.Linq;

public class GameKey : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        Dictionary<string, Dictionary<string, object>> allKeys = new Dictionary<string, Dictionary<string, object>>();
        Reader reader = new Reader(data, pos);
        int keySum = (int)reader.Read(new VarInt());
        
        for (int i = 0; i < keySum; i++)
        {
            string name = (string)reader.Read(new String());
            int length = (byte)reader.Read(new Byte());
            Dictionary<string, object> oneKey = new Dictionary<string, object>();
            allKeys[name] = oneKey;
            
            oneKey["type"] = reader.Read(new Bits(5));
            List<byte> flags = new List<byte>();
            
            for (int j = 0; j < length - 1; j++)
            {
                flags.Add((byte)reader.Read(new Byte()));
            }
            oneKey["flag"] = flags;
        }
        
        pos = reader.Position;
        return allKeys;
    }

    public void Write(List<byte> data, object value)
    {
        var allKeys = (Dictionary<string, Dictionary<string, object>>)value;
        Writer writer = new Writer();
        writer.Write(new VarInt(), allKeys.Count);
        
        foreach (var kvp in allKeys)
        {
            writer.Write(new String(), kvp.Key);
            var flags = (List<byte>)kvp.Value["flag"];
            writer.Write(new Byte(), flags.Count + 1);
            writer.Write(new Bits(), kvp.Value["type"]);
            
            foreach (byte flag in flags)
            {
                writer.Write(new Byte(), flag);
            }
        }
        
        data.AddRange(writer.GetData());
    }
}

public class GameKey02 : GameKey { } // 版本2
public class GameKey03 : GameKey { } // 版本3

public class Money : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        List<int> money = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            money.Add((int)new VarInt().Read(data, ref pos));
        }
        return money;
    }

    public void Write(List<byte> data, object value)
    {
        var money = (List<int>)value;
        foreach (int m in money)
        {
            new VarInt().Write(data, m);
        }
    }
}

public class GameRecord : IDataType
{
    private static readonly string[] DiffList = { "EZ", "HD", "IN", "AT", "Legacy" };
    
    public object Read(byte[] data, ref int pos)
    {
        Dictionary<string, Dictionary<string, Dictionary<string, object>>> allRecords = 
            new Dictionary<string, Dictionary<string, Dictionary<string, object>>>();
        
        Reader reader = new Reader(data, pos);
        int songSum = (int)reader.Read(new VarInt());
        
        for (int i = 0; i < songSum; i++)
        {
            string songName = ((string)reader.Read(new String())).Replace(".0", "");
            int length = (int)reader.Read(new VarInt());
            int endPos = reader.Position + length;
            
            byte unlock = (byte)reader.Read(new Byte());
            byte fc = (byte)reader.Read(new Byte());
            
            Dictionary<string, Dictionary<string, object>> song = new Dictionary<string, Dictionary<string, object>>();
            allRecords[songName] = song;
            
            for (int level = 0; level < 5; level++)
            {
                if (DataType.ReadBit(unlock, level) == 1)
                {
                    int score = (int)reader.Read(new Int());
                    float acc = (float)reader.Read(new Float());
                    
                    song[DiffList[level]] = new Dictionary<string, object>
                    {
                        { "score", score },
                        { "acc", acc },
                        { "fc", DataType.ReadBit(fc, level) }
                    };
                }
            }
        }
        
        pos = reader.Position;
        return allRecords;
    }

    public void Write(List<byte> data, object value)
    {
        var records = (Dictionary<string, Dictionary<string, Dictionary<string, object>>>)value;
        Writer writer = new Writer();
        writer.Write(new VarInt(), records.Count);
        
        foreach (var record in records)
        {
            writer.Write(new String(), record.Key + ".0");
            
            int songSize = record.Value.Count * 8 + 2;
            writer.Write(new VarInt(), songSize);
            
            List<int> unlock = Enumerable.Repeat(0, 8).ToList();
            List<int> fc = Enumerable.Repeat(0, 8).ToList();
            Writer recordWriter = new Writer();
            
            foreach (var diff in record.Value)
            {
                int index = System.Array.IndexOf(DiffList, diff.Key);
                if (index >= 0)
                {
                    unlock[index] = 1;
                    recordWriter.Write(new Int(), diff.Value["score"]);
                    recordWriter.Write(new Float(), diff.Value["acc"]);
                    fc[index] = (int)diff.Value["fc"];
                }
            }
            
            writer.Write(new Bits(), unlock);
            writer.Write(new Bits(), fc);
            writer.Write(new Byte(), recordWriter.GetData());
        }
        
        data.AddRange(writer.GetData());
    }
}

public class gameProgress03 : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        // 实现类似gameProgress04，但缺少flagOfSongRecordKeyTakumi
        // ...
        return null;
    }

    public void Write(List<byte> data, object value)
    {
        // ...
    }
}

public class gameProgress04 : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        Reader reader = new Reader(data, pos);
        Dictionary<string, object> progress = new Dictionary<string, object>();
        
        progress["isFirstRun"] = reader.Read(new Bit());
        progress["legacyChapterFinished"] = reader.Read(new Bit());
        progress["alreadyShowCollectionTip"] = reader.Read(new Bit());
        progress["alreadyShowAutoUnlockINTip"] = reader.Read(new Bit());
        progress["completed"] = reader.Read(new String());
        progress["songUpdateInfo"] = reader.Read(new VarInt());
        progress["challengeModeRank"] = reader.Read(new ShortInt());
        progress["money"] = reader.Read(new Money());
        progress["unlockFlagOfSpasmodic"] = reader.Read(new Bits(4));
        progress["unlockFlagOfIgallta"] = reader.Read(new Bits(4));
        progress["unlockFlagOfRrharil"] = reader.Read(new Bits(4));
        progress["flagOfSongRecordKey"] = reader.Read(new Bits());
        progress["randomVersionUnlocked"] = reader.Read(new Bits(6));
        progress["chapter8UnlockBegin"] = reader.Read(new Bit());
        progress["chapter8UnlockSecondPhase"] = reader.Read(new Bit());
        progress["chapter8Passed"] = reader.Read(new Bit());
        progress["chapter8SongUnlocked"] = reader.Read(new Bits(6));
        progress["flagOfSongRecordKeyTakumi"] = reader.Read(new Bits(3));
        
        pos = reader.Position;
        return progress;
    }

    public void Write(List<byte> data, object value)
    {
        var progress = (Dictionary<string, object>)value;
        Writer writer = new Writer();
        
        writer.Write(new Bit(), progress["isFirstRun"]);
        writer.Write(new Bit(), progress["legacyChapterFinished"]);
        writer.Write(new Bit(), progress["alreadyShowCollectionTip"]);
        writer.Write(new Bit(), progress["alreadyShowAutoUnlockINTip"]);
        writer.Write(new String(), progress["completed"]);
        writer.Write(new VarInt(), progress["songUpdateInfo"]);
        writer.Write(new ShortInt(), progress["challengeModeRank"]);
        writer.Write(new Money(), progress["money"]);
        writer.Write(new Bits(4), progress["unlockFlagOfSpasmodic"]);
        writer.Write(new Bits(4), progress["unlockFlagOfIgallta"]);
        writer.Write(new Bits(4), progress["unlockFlagOfRrharil"]);
        writer.Write(new Bits(), progress["flagOfSongRecordKey"]);
        writer.Write(new Bits(6), progress["randomVersionUnlocked"]);
        writer.Write(new Bit(), progress["chapter8UnlockBegin"]);
        writer.Write(new Bit(), progress["chapter8UnlockSecondPhase"]);
        writer.Write(new Bit(), progress["chapter8Passed"]);
        writer.Write(new Bits(6), progress["chapter8SongUnlocked"]);
        writer.Write(new Bits(3), progress["flagOfSongRecordKeyTakumi"]);
        
        data.AddRange(writer.GetData());
    }
}

public class settings01 : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        Reader reader = new Reader(data, pos);
        Dictionary<string, object> settings = new Dictionary<string, object>();
        
        settings["chordSupport"] = reader.Read(new Bit());
        settings["fcAPIndicator"] = reader.Read(new Bit());
        settings["enableHitSound"] = reader.Read(new Bit());
        settings["lowResolutionMode"] = reader.Read(new Bit());
        settings["deviceName"] = reader.Read(new String());
        settings["bright"] = reader.Read(new Float());
        settings["musicVolume"] = reader.Read(new Float());
        settings["effectVolume"] = reader.Read(new Float());
        settings["hitSoundVolume"] = reader.Read(new Float());
        settings["soundOffset"] = reader.Read(new Float());
        settings["noteScale"] = reader.Read(new Float());
        
        pos = reader.Position;
        return settings;
    }

    public void Write(List<byte> data, object value)
    {
        var settings = (Dictionary<string, object>)value;
        Writer writer = new Writer();
        
        writer.Write(new Bit(), settings["chordSupport"]);
        writer.Write(new Bit(), settings["fcAPIndicator"]);
        writer.Write(new Bit(), settings["enableHitSound"]);
        writer.Write(new Bit(), settings["lowResolutionMode"]);
        writer.Write(new String(), settings["deviceName"]);
        writer.Write(new Float(), settings["bright"]);
        writer.Write(new Float(), settings["musicVolume"]);
        writer.Write(new Float(), settings["effectVolume"]);
        writer.Write(new Float(), settings["hitSoundVolume"]);
        writer.Write(new Float(), settings["soundOffset"]);
        writer.Write(new Float(), settings["noteScale"]);
        
        data.AddRange(writer.GetData());
    }
}

public class user01 : IDataType
{
    public object Read(byte[] data, ref int pos)
    {
        Reader reader = new Reader(data, pos);
        Dictionary<string, object> user = new Dictionary<string, object>();
        
        user["showPlayerId"] = reader.Read(new Byte());
        user["selfIntro"] = reader.Read(new String());
        user["avatar"] = reader.Read(new String());
        user["background"] = reader.Read(new String());
        
        pos = reader.Position;
        return user;
    }

    public void Write(List<byte> data, object value)
    {
        var user = (Dictionary<string, object>)value;
        Writer writer = new Writer();
        
        writer.Write(new Byte(), user["showPlayerId"]);
        writer.Write(new String(), user["selfIntro"]);
        writer.Write(new String(), user["avatar"]);
        writer.Write(new String(), user["background"]);
        
        data.AddRange(writer.GetData());
    }
}