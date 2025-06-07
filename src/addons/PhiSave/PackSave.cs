using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

[GlobalClass]
public partial class PackSave : RefCounted
{
    private static readonly byte[] AesKey = Convert.FromBase64String("6Jaa0qVAJZuXkZCLiOa/Ax5tIZVu+taKUN1V1nqwkks=");
    private static readonly byte[] AesIv = Convert.FromBase64String("Kk/wisgNYwcAV8WVGMgyUw==");
    private static readonly string[] SaveFiles = { "gameKey", "gameProgress", "gameRecord", "settings", "user" };

    public void GetSave(string source = "user://.save", string output = "user://PhigrosSaves.json")
    {
        try
        {
            using var file = Godot.FileAccess.Open(source, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PushError($"无法打开存档文件: {source}");
                return;
            }

            byte[] saveData = file.GetBuffer((long)file.GetLength());
            
            // 添加调试信息
            GD.Print($"读取存档文件大小: {saveData.Length} 字节");
            
            Dictionary<string, object> saveDict = ParseSave(saveData);
            Godot.Collections.Dictionary godotDict = new Godot.Collections.Dictionary();

            foreach (var kvp in saveDict)
            {
                godotDict[kvp.Key] = (Variant)kvp.Value;
            }

            using var outputFile = Godot.FileAccess.Open(output, Godot.FileAccess.ModeFlags.Write);
            if (outputFile != null)
            {
                outputFile.StoreString(Json.Stringify(godotDict));
                GD.Print($"成功保存 JSON 到: {output}");
            }
            else
            {
                GD.PushError($"无法打开输出文件: {output}");
            }
        }
        catch (Exception e)
        {
            GD.PushError($"获取存档时出错: {e.Message}\n{e.StackTrace}");
        }
    }

    public void Upload(string source = "user://PhigrosSaves.json", string output = "user://.save")
    {
        try
        {
            using var file = Godot.FileAccess.Open(source, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PushError($"无法打开 JSON 文件: {source}");
                return;
            }

            string json = file.GetAsText();
            var saveDict = (Godot.Collections.Dictionary)Json.ParseString(json);
            
            // 添加调试信息
            GD.Print($"解析 JSON 成功，键数量: {saveDict.Count}");
            
            byte[] saveData = BuildSave(saveDict);

            using var outputFile = Godot.FileAccess.Open(output, Godot.FileAccess.ModeFlags.Write);
            if (outputFile != null)
            {
                outputFile.StoreBuffer(saveData);
                GD.Print($"成功保存存档到: {output}");
            }
            else
            {
                GD.PushError($"无法打开输出文件: {output}");
            }
        }
        catch (Exception e)
        {
            GD.PushError($"上传存档时出错: {e.Message}\n{e.StackTrace}");
        }
    }

    private byte[] BuildSave(Godot.Collections.Dictionary saveDict)
    {
        throw new NotImplementedException();
    }


    private static Dictionary<string, object> ParseSave(byte[] saveData)
    {
        Dictionary<string, byte[]> unzipped = UnzipSave(saveData);
        return DecryptSave(unzipped);
    }

    private static byte[] BuildSave(Dictionary<string, object> saveDict)
    {
        Dictionary<string, byte[]> encrypted = EncryptSave(saveDict);
        return ZipSave(encrypted);
    }

    private static Dictionary<string, byte[]> UnzipSave(byte[] zipData)
    {
        Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
        using var stream = new MemoryStream(zipData);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            result[entry.Name] = ms.ToArray();
            
            // 添加调试信息
            GD.Print($"解压文件: {entry.Name}, 大小: {ms.Length} 字节");
        }
        
        return result;
    }

    private static byte[] ZipSave(Dictionary<string, byte[]> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key);
                using var entryStream = entry.Open();
                entryStream.Write(file.Value, 0, file.Value.Length);
                
                // 添加调试信息
                GD.Print($"压缩文件: {file.Key}, 大小: {file.Value.Length} 字节");
            }
        }
        return ms.ToArray();
    }

    private static Dictionary<string, object> DecryptSave(Dictionary<string, byte[]> saveDict)
    {
        Dictionary<string, object> result = new Dictionary<string, object>();
        foreach (var kvp in saveDict)
        {
            try
            {
                byte[] decrypted = Decrypt(kvp.Value);
                byte header = decrypted[0];
                IDataType reader = GetDataType(kvp.Key, header);
                int pos = 1;
                result[kvp.Key] = reader.Read(decrypted, ref pos);
                
                // 添加调试信息
                GD.Print($"解密文件成功: {kvp.Key}");
            }
            catch (Exception e)
            {
                GD.PushError($"解密文件 {kvp.Key} 时出错: {e.Message}");
            }
        }
        return result;
    }

    private static Dictionary<string, byte[]> EncryptSave(Dictionary<string, object> saveDict)
    {
        Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
        foreach (var fileName in SaveFiles)
        {
            if (!saveDict.ContainsKey(fileName)) continue;
            
            byte header = GetFileHeader(fileName, saveDict);
            List<byte> data = new List<byte> { header };
            
            IDataType writer = GetDataType(fileName, header);
            writer.Write(data, saveDict[fileName]);
            
            byte[] encrypted = Encrypt(data.ToArray());
            result[fileName] = encrypted;
            
            // 添加调试信息
            GD.Print($"加密文件: {fileName}, 原始大小: {data.Count} 字节, 加密后大小: {encrypted.Length} 字节");
        }
        return result;
    }

    private static IDataType GetDataType(string fileName, byte header)
    {
        // 简化版本，实际应根据header返回适当的数据类型
        return new GameKey();
    }

    private static byte GetFileHeader(string fileName, Dictionary<string, object> saveDict)
    {
        // 简化版本，返回默认header
        return 0x01;
    }

    private static byte[] Encrypt(byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        // 添加调试信息
        GD.Print($"加密前数据大小: {data.Length} 字节");
        
        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
        
        // 添加调试信息
        GD.Print($"加密后数据大小: {encrypted.Length} 字节");
        
        return encrypted;
    }

    private static byte[] Decrypt(byte[] data)
    {
        using Aes aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        // 添加调试信息
        GD.Print($"解密前数据大小: {data.Length} 字节");
        
        // 修复：确保数据长度是块大小的倍数
        if (data.Length % aes.BlockSize != 0)
        {
            GD.PushError($"数据长度 {data.Length} 不是块大小 {aes.BlockSize} 的倍数，尝试修复...");
            
            // 计算需要填充的字节数
            int padding = aes.BlockSize - (data.Length % aes.BlockSize);
            GD.Print($"需要填充 {padding} 字节");
            
            // 创建新数组并填充
            byte[] paddedData = new byte[data.Length + padding];
            Array.Copy(data, paddedData, data.Length);
            
            // 使用填充后的数据
            data = paddedData;
        }

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }
}