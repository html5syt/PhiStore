using System;
using System.IO;
using System.Security.Cryptography;
using System.IO.Compression;
using PhiStore.Addons.PhiSave2.Models;
using System.Text.Json;

namespace PhiStore.Addons.PhiSave2.Internal;

/// <summary>
/// AES 加密工具类，用于本地持久化保护
/// 使用 AES-256-CBC
/// </summary>
public static class AESUtil
{
    private static readonly Lazy<Aes> AesProvider = new Lazy<Aes>(() =>
    {
        var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 256;
        aes.BlockSize = 128;
        return aes;
    });

    /// <summary>
    /// 生成随机の密钥和IV
    /// </summary>
    public static (byte[] Key, byte[] IV) GenerateKeys()
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();
        return (aes.Key, aes.IV);
    }

    /// <summary>
    /// 加密：明文 -> GZip -> AES -> 密文
    /// </summary>
    public static byte[] Encrypt(byte[] rawData, byte[] key, byte[] iv)
    {
        using var msOut = new MemoryStream();
        var aes = AesProvider.Value;
        using (var encryptor = aes.CreateEncryptor(key, iv))
        using (var cs = new CryptoStream(msOut, encryptor, CryptoStreamMode.Write))
        using (var gzip = new GZipStream(cs, CompressionLevel.Optimal))
        {
            gzip.Write(rawData, 0, rawData.Length);
        }
        return msOut.ToArray();
    }

    /// <summary>
    /// 解密：密文 -> AES -> GZip -> 明文
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv)
    {
        using var msIn = new MemoryStream(encryptedData);
        var aes = AesProvider.Value;
        using var decryptor = aes.CreateDecryptor(key, iv);
        using var cs = new CryptoStream(msIn, decryptor, CryptoStreamMode.Read);
        using var gzip = new GZipStream(cs, CompressionMode.Decompress);
        using var msOut = new MemoryStream();
        gzip.CopyTo(msOut);
        return msOut.ToArray();
    }

    /// <summary>
    /// 将对象序列化为 JSON 文本，加密并保存到指定路径
    /// </summary>
    public static void SaveEncrypted(string filePath, PhiSaveData data, byte[] key, byte[] iv)
    {
        var jsonText = JsonSerializer.Serialize(data, PhiSaveJsonContext.Default.PhiSaveData);
        var rawData = System.Text.Encoding.UTF8.GetBytes(jsonText);
        var encryptedData = Encrypt(rawData, key, iv);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(filePath, encryptedData);
    }

    /// <summary>
    /// 从指定路径读取并解密 JSON 文本，反序列化为对象
    /// </summary>
    public static PhiSaveData? LoadDecrypted(string filePath, byte[] key, byte[] iv)
    {
        if (!File.Exists(filePath)) return default;

        var encryptedData = File.ReadAllBytes(filePath);
        var rawData = Decrypt(encryptedData, key, iv);
        var jsonText = System.Text.Encoding.UTF8.GetString(rawData);
        return JsonSerializer.Deserialize(jsonText, PhiSaveJsonContext.Default.PhiSaveData);
    }
}
