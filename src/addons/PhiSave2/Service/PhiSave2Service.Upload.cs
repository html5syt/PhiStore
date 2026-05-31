using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.Serialization;

// PhiSave2Service：云端存档上传
namespace PhiStore.Addons.PhiSave2;

public partial class PhiSave2Service
{    
    /// <summary>
     /// 将内存中的当前存档打包并上传到云端：
     /// 1. 创建文件令牌（file token）并发起分片上传；
     /// 2. 完成上传后回调并将 Summary 更新到 _GameSave 类；
     /// 3. 可选地删除旧的 game file 对象。
     /// </summary>
     /// <param name="oldSaveGameFileObjectId">可选的旧 game file 对象 ID，用于在成功上传后删除旧对象。</param>
     /// <param name="oldSaveObjectId">可选的旧保存记录对象 ID（用于更新 summary）。</param>
     /// <param name="packedSaveBuffer">已打包的 Save ZIP 字节数组。</param>
     /// <param name="packedSummaryBuffer">Summary 的二进制表示（用于写入 summary 字段）。</param>
    public async Task UploadSaveAsync(string? oldSaveGameFileObjectId, string? oldSaveObjectId, byte[] packedSaveBuffer, byte[] packedSummaryBuffer)
    {
        if (_saveObj == null) throw new InvalidOperationException("Not initialized");
        if (CurrentSave == null) throw new InvalidOperationException("No save in memory to upload.");
        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            throw new InvalidOperationException("Missing user object id for upload.");
        }

        FileTokenInfo token = await CreateFileTokenAsync(packedSaveBuffer, _userObjectId);
        CreateUploadResponse uploadInfo = await CreateUploadAsync(token);

        (int, RequestUploadPart)[] parts = [(1, await UploadPartAsync(1, packedSaveBuffer, token, uploadInfo))];
        await CompleteUploadAsync(token, uploadInfo, parts);
        await UpdateSummaryAsync(token, packedSummaryBuffer, oldSaveObjectId, _userObjectId);

        if (!string.IsNullOrEmpty(oldSaveGameFileObjectId))
            await DeleteOldAsync(oldSaveGameFileObjectId);
    }

    /// <summary>
    /// 上传内存存档到云端
    /// 该方法相比较于重载版本会直接从当前内存中的存档构建上传所需的上下文并打包成 ZIP，因此不需要调用方提供已打包的字节数组。
    /// </summary>
    public async Task UploadSaveAsync(string? oldFileId, string? oldObjId)
    {
        if (_saveObj == null || CurrentSave == null) throw new InvalidOperationException("Missing state");

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(_sessionToken))
        {
            try
            {
                var fetched = await FetchUserObjectIdFromServerAsync();
                if (!string.IsNullOrWhiteSpace(fetched)) _userObjectId = fetched;
            }
            catch { }
        }

        // 获取原有的 SaveInfo 以构建上下文
        var container = await _saveObj.GetSaveInfoFromCloudAsync();
        var originalInfo = container.Results.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(_userObjectId) && !string.IsNullOrWhiteSpace(originalInfo?.User?.ObjectId))
        {
            _userObjectId = originalInfo.User.ObjectId;
        }

        if (string.IsNullOrWhiteSpace(_userObjectId))
        {
            throw new InvalidOperationException("Missing user object id for upload.");
        }

        var ctx = new SaveContext(new Dictionary<string, SaveContext.Entry>(), originalInfo!);
        CurrentSave.WriteToContext(ctx);

        // 构建 Summary：在打包前确保 ctx 中的 Summary 已更新为 CurrentSave 的值
        try
        {
            var existing = ctx.ReadSummary();
            if (existing != null)
            {
                existing.Rks = CurrentSave.SummaryRks;
                ctx.SaveSummary(existing);
            }
        }
        catch { }

        byte[] packedSave;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                SaveContext.CipherFunction encryptor = (data, ct) =>
                {
                    if (_saveObj == null) return Task.FromResult(data);
                    return _saveObj.Encrypt(data, ct);
                };
                await ctx.SaveToZipAsync(archive, encryptor, CancellationToken.None);
            }
            packedSave = ms.ToArray();
        }

        byte[] packedSummary = Array.Empty<byte>();
        try
        {
            var ctxSummary = ctx.ReadSummary();
            if (ctxSummary != null)
            {
                using var ms2 = new MemoryStream();
                var bw = new ByteWriter(ms2);
                ctxSummary.Serialize(bw);
                packedSummary = ms2.ToArray();
            }
        }
        catch { }

        await UploadSaveAsync(oldFileId, oldObjId, packedSave, packedSummary);
    }

    private static string BuildApiUrl(string baseUrl, string relativePath)
    {
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        var builder = new UriBuilder(baseUri)
        {
            Path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

    private static string BuildUploadUrl(FileTokenInfo info, string? suffix = null)
    {
        var baseUri = new Uri(info.upload_url, UriKind.Absolute);
        var tokenKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key));
        var path = $"/buckets/{Uri.EscapeDataString(info.bucket)}/objects/{tokenKey}/uploads";
        if (!string.IsNullOrEmpty(suffix)) path += suffix.StartsWith('/') ? suffix : "/" + suffix;
        var builder = new UriBuilder(baseUri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

    private async Task<FileTokenInfo> CreateFileTokenAsync(byte[] packedSaveBuffer, string userObjectId)
    {
        HttpClient client = _saveObj!.Client;

        var fileTokenRequest = new FileTokenRequest
        {
            name = ".save",
            __type = "File",
            ACL = new Dictionary<string, AclPermission>(),
            prefix = "gamesaves",
            metaData = new FileTokenMeta(
                Convert.ToHexString(MD5.HashData(packedSaveBuffer)),
                "gamesaves",
                packedSaveBuffer.Length
            )
        };
        fileTokenRequest.ACL[userObjectId] = new AclPermission
        {
            read = true,
            write = true
        };

        var content = new StringContent(JsonSerializer.Serialize(fileTokenRequest, PhiSaveJsonContext.Default.FileTokenRequest), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(BuildApiUrl(Save.CloudServerAddress, "/1.1/fileTokens"), content);
        response.EnsureSuccessStatusCode();

        return (FileTokenInfo)(await response.Content.ReadFromJsonAsync(typeof(FileTokenInfo), PhiSaveJsonContext.Default))!;
    }

    private async Task<CreateUploadResponse> CreateUploadAsync(FileTokenInfo info)
    {
        HttpClient client = _saveObj!.Client;
        HttpRequestMessage request = new(HttpMethod.Post, BuildUploadUrl(info));
        request.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (CreateUploadResponse)(await response.Content.ReadFromJsonAsync(typeof(CreateUploadResponse), PhiSaveJsonContext.Default))!;
    }

    private async Task<RequestUploadPart> UploadPartAsync(int partNumber, byte[] packedSaveBuffer, FileTokenInfo info, CreateUploadResponse uploadCreation)
    {
        HttpClient client = _saveObj!.Client;
        HttpRequestMessage request = new(HttpMethod.Put, BuildUploadUrl(info, $"/{uploadCreation.uploadId}/{partNumber}"))
        {
            Content = new ByteArrayContent(packedSaveBuffer)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (RequestUploadPart)(await response.Content.ReadFromJsonAsync(typeof(RequestUploadPart), PhiSaveJsonContext.Default))!;
    }

    private async Task CompleteUploadAsync(FileTokenInfo info, CreateUploadResponse uploadCreation, params (int Index, RequestUploadPart Part)[] parts)
    {
        HttpClient client = _saveObj!.Client;
        var completeReq = new CompleteUploadRequest
        {
            parts = parts.Select(x => new CompleteUploadPart { partNumber = x.Index, etag = x.Part.etag }).ToArray()
        };
        var c1 = new StringContent(JsonSerializer.Serialize(completeReq, PhiSaveJsonContext.Default.CompleteUploadRequest), Encoding.UTF8, "application/json");
        HttpRequestMessage request1 = new(HttpMethod.Post, BuildUploadUrl(info, $"/{uploadCreation.uploadId}"))
        {
            Content = c1
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        HttpResponseMessage response1 = await client.SendAsync(request1);
        response1.EnsureSuccessStatusCode();

        var callbackReq = new FileCallbackRequest
        {
            result = true,
            token = Convert.ToHexString(Encoding.UTF8.GetBytes(info.key))
        };
        var c2 = new StringContent(JsonSerializer.Serialize(callbackReq, PhiSaveJsonContext.Default.FileCallbackRequest), Encoding.UTF8, "application/json");
        HttpRequestMessage request2 = new(HttpMethod.Post, BuildApiUrl(Save.CloudServerAddress, "/1.1/fileCallback"))
        {
            Content = c2
        };
        HttpResponseMessage response2 = await client.SendAsync(request2);
        response2.EnsureSuccessStatusCode();
    }

    private async Task UpdateSummaryAsync(FileTokenInfo info, byte[] packedSummaryData, string? oldSaveObjectId, string userObjectId)
    {
        HttpClient client = _saveObj!.Client;

        var requestData = new UpdateSummaryRequest
        {
            summary = Convert.ToBase64String(packedSummaryData),
            modifiedAt = new UpdateSummaryDate
            {
                iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.FFFZ", CultureInfo.InvariantCulture)
            },
            gameFile = new UpdateSummaryPointer
            {
                className = "_File",
                objectId = info.objectId
            },
            user = new UpdateSummaryPointer
            {
                className = "_User",
                objectId = userObjectId
            }
        };
        requestData.ACL[userObjectId] = new AclPermission
        {
            read = true,
            write = true,
        };
        string url = BuildApiUrl(Save.CloudServerAddress, "/1.1/classes/_GameSave");
        HttpMethod method = HttpMethod.Post;
        if (!string.IsNullOrEmpty(oldSaveObjectId))
        {
            url += $"/{oldSaveObjectId}";
            method = HttpMethod.Put;
        }
        var c3 = new StringContent(JsonSerializer.Serialize(requestData, PhiSaveJsonContext.Default.UpdateSummaryRequest), Encoding.UTF8, "application/json");
        HttpRequestMessage request = new(method, url)
        {
            Content = c3
        };
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task DeleteOldAsync(string oldSaveGameFileObjectId)
    {
        HttpClient client = _saveObj!.Client;
        HttpResponseMessage response = await client.DeleteAsync(BuildApiUrl(Save.CloudServerAddress, $"/1.1/files/{oldSaveGameFileObjectId}"));
        response.EnsureSuccessStatusCode();
    }
}
