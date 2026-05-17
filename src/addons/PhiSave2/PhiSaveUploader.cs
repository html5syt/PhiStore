using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PhigrosLibraryCSharp.CloudSave;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 存档上传相关流水线
/// </summary>
public static class PhiSaveUploader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = null,
        TypeInfoResolver = PhiSaveJsonContext.Default
    };

    public record struct FileTokenMeta(string _checksum, string prefix, int size);
    public record struct FileTokenInfo(
        string bucket,
        string createdAt,
        string key,
        FileTokenMeta metaData,
        string mime_type,
        string name,
        string objectId,
        string provider,
        string token,
        string upload_url,
        string url);
    public record struct CreateUploadResponse(string uploadId, object expireAt);
    public record struct RequestUploadPart(string etag, string md5);

    public class FileTokenRequest
    {
        public string name { get; set; } = string.Empty;
        public string __type { get; set; } = string.Empty;
        public Dictionary<string, AclPermission> ACL { get; set; } = new();
        public string prefix { get; set; } = string.Empty;
        public FileTokenMeta metaData { get; set; }
    }

    public class AclPermission
    {
        public bool read { get; set; }
        public bool write { get; set; }
    }

    public class CompleteUploadRequest
    {
        public CompleteUploadPart[] parts { get; set; } = Array.Empty<CompleteUploadPart>();
    }

    public class CompleteUploadPart
    {
        public int partNumber { get; set; }
        public string etag { get; set; } = string.Empty;
    }

    public class FileCallbackRequest
    {
        public bool result { get; set; }
        public string token { get; set; } = string.Empty;
    }

    public class UpdateSummaryRequest
    {
        public string summary { get; set; } = string.Empty;
        public UpdateSummaryDate modifiedAt { get; set; } = new();
        public UpdateSummaryPointer gameFile { get; set; } = new();
        public Dictionary<string, AclPermission> ACL { get; set; } = new();
        public UpdateSummaryPointer user { get; set; } = new();
    }

    public class UpdateSummaryDate
    {
        public string __type { get; set; } = "Date";
        public string iso { get; set; } = string.Empty;
    }

    public class UpdateSummaryPointer
    {
        public string __type { get; set; } = "Pointer";
        public string className { get; set; } = string.Empty;
        public string objectId { get; set; } = string.Empty;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Client")]
    private static extern HttpClient GetHttpClient(Save save);

    private static JsonSerializerOptions _jsonOpts = new JsonSerializerOptions
    {
        TypeInfoResolver = PhiSaveJsonContext.Default
    };

    public static async Task UploadSaveAsync(Save save, string userObjectId, string? oldSaveGameFileObjectId, string? oldSaveObjectId, byte[] packedSaveBuffer, byte[] packedSummaryBuffer)
    {
        FileTokenInfo token = await CreateFileTokenAsync(packedSaveBuffer, userObjectId, save);
        CreateUploadResponse uploadInfo = await CreateUploadAsync(token, save);

        (int, RequestUploadPart)[] parts = [(1, await UploadPartAsync(1, packedSaveBuffer, token, uploadInfo, save))];
        await CompleteUploadAsync(token, uploadInfo, save, parts);
        await UpdateSummaryAsync(token, packedSummaryBuffer, oldSaveObjectId, userObjectId, save);

        if (!string.IsNullOrEmpty(oldSaveGameFileObjectId))
            await DeleteOldAsync(oldSaveGameFileObjectId, save);
    }

    private static async Task<FileTokenInfo> CreateFileTokenAsync(byte[] packedSaveBuffer, string userObjectId, Save save)
    {
        HttpClient client = GetHttpClient(save);

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
        HttpResponseMessage response = await client.PostAsync($"{Save.CloudServerAddress}/1.1/fileTokens", content);
        response.EnsureSuccessStatusCode();

        return (FileTokenInfo)(await response.Content.ReadFromJsonAsync(typeof(FileTokenInfo), PhiSaveJsonContext.Default))!;
    }

    private static async Task<CreateUploadResponse> CreateUploadAsync(FileTokenInfo info, Save save)
    {
        HttpClient client = GetHttpClient(save);
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://upload.qiniup.com/buckets/rAK3Ffdi/objects/{Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key))}/uploads");
        request.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (CreateUploadResponse)(await response.Content.ReadFromJsonAsync(typeof(CreateUploadResponse), PhiSaveJsonContext.Default))!;
    }

    private static async Task<RequestUploadPart> UploadPartAsync(int partNumber, byte[] packedSaveBuffer, FileTokenInfo info, CreateUploadResponse uploadCreation, Save save)
    {
        HttpClient client = GetHttpClient(save);
        HttpRequestMessage request = new(
            HttpMethod.Put,
            $"https://upload.qiniup.com/buckets/rAK3Ffdi/objects/{Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key))}/uploads/{uploadCreation.uploadId}/{partNumber}")
        {
            Content = new ByteArrayContent(packedSaveBuffer)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (RequestUploadPart)(await response.Content.ReadFromJsonAsync(typeof(RequestUploadPart), PhiSaveJsonContext.Default))!;
    }

    private static async Task CompleteUploadAsync(FileTokenInfo info, CreateUploadResponse uploadCreation, Save save, params (int Index, RequestUploadPart Part)[] parts)
    {
        HttpClient client = GetHttpClient(save);
        var completeReq = new CompleteUploadRequest
        {
            parts = parts.Select(x => new CompleteUploadPart { partNumber = x.Index, etag = x.Part.etag }).ToArray()
        };
        var c1 = new StringContent(JsonSerializer.Serialize(completeReq, PhiSaveJsonContext.Default.CompleteUploadRequest), Encoding.UTF8, "application/json");
        HttpRequestMessage request1 = new(HttpMethod.Post, $"https://upload.qiniup.com/buckets/rAK3Ffdi/objects/" +
            $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key))}/uploads/{uploadCreation.uploadId}")
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
        HttpRequestMessage request2 = new(HttpMethod.Post, @"https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/fileCallback")
        {
            Content = c2
        };
        HttpResponseMessage response2 = await client.SendAsync(request2);
        response2.EnsureSuccessStatusCode();
    }

    private static async Task UpdateSummaryAsync(FileTokenInfo info, byte[] packedSummaryData, string? oldSaveObjectId, string userObjectId, Save save)
    {
        HttpClient client = GetHttpClient(save);

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
        string url = @"https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/classes/_GameSave";
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

    private static async Task DeleteOldAsync(string oldSaveGameFileObjectId, Save save)
    {
        HttpClient client = GetHttpClient(save);
        HttpResponseMessage response = await client.DeleteAsync($"https://rak3ffdi.cloud.tds1.tapapis.cn/1.1/files/{oldSaveGameFileObjectId}");
        response.EnsureSuccessStatusCode();
    }
}