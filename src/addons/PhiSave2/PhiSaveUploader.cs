#nullable enable
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PhigrosLibraryCSharp;

namespace PhiStore.Addons.PhiSave2;

/// <summary>
/// 提供与云端文件 Token / 上传 / 回调 / 删除 相关的静态工具方法。
/// 此类内部通过 UnsafeAccessor 访问 Save 的 HttpClient（来自 PhigrosLibraryCSharp）。
/// </summary>
internal static class PhiSaveUploader
{
    private record struct FileTokenMeta(string _checksum, string prefix, int size);
    private record struct FileTokenInfo(
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
    private record struct CreateUploadResponse(string uploadId, object expireAt);
    private record struct RequestUploadPart(string etag, string md5);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

#pragma warning disable IL2026, IL3050

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Client")]
    private static extern HttpClient GetHttpClient(Save save);

    /// <summary>
    /// 获取云端原始 _GameSave 列表并解析为 JsonNode，供上层选择目标存档条目。
    /// </summary>
    /// <param name="save">已初始化的 Save 实例。</param>
    /// <returns>解析后的 JsonNode（包含 results 数组）。</returns>
    public static async Task<JsonNode> FetchRawSaveAsNode(Save save)
    {
        HttpClient client = GetHttpClient(save);
        string baseUrl = Save.GetCloudServerAddress(!save.IsInternational);
        HttpResponseMessage response = await client.GetAsync($"{baseUrl}/1.1/classes/_GameSave");
        response.EnsureSuccessStatusCode();
        string content = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(content) ?? throw new InvalidOperationException("Failed to parse raw save JSON.");
    }

    /// <summary>
    /// 将已打包好的存档与 summary 上传到云端（包含创建 file token、上传分片、完成上传以及更新 summary）。
    /// </summary>
    /// <param name="save">已初始化的 Save 实例。</param>
    /// <param name="userObjectId">目标用户 objectId。</param>
    /// <param name="oldSaveGameFileObjectId">若存在旧文件 objectId 则用于删除旧资源（可 null）。</param>
    /// <param name="oldSaveObjectId">若存在旧 save objectId 则用于更新（可 null）。</param>
    /// <param name="packedSaveBuffer">打包并加密后的 game file 字节数组。</param>
    /// <param name="packedSummaryBuffer">打包后的 summary 字节数组。</param>
    public static async Task UploadSave(
        Save save,
        string userObjectId,
        string? oldSaveGameFileObjectId,
        string? oldSaveObjectId,
        byte[] packedSaveBuffer,
        byte[] packedSummaryBuffer)
    {
        FileTokenInfo token = await CreateFileToken(packedSaveBuffer, userObjectId, save);
        CreateUploadResponse uploadInfo = await CreateUpload(token, save);

        (int, RequestUploadPart)[] parts =
        [
            (1, await UploadPart(1, packedSaveBuffer, token, uploadInfo, save))
        ];
        await CompleteUpload(token, uploadInfo, save, parts);
        await UpdateSummary(token, packedSummaryBuffer, oldSaveObjectId, userObjectId, save);
        if (oldSaveGameFileObjectId is not null)
            await DeleteOld(oldSaveGameFileObjectId, save);
    }

    private static async Task<FileTokenInfo> CreateFileToken(byte[] packedSaveBuffer, string userObjectId, Save save)
    {
        HttpClient client = GetHttpClient(save);
        string baseUrl = Save.GetCloudServerAddress(!save.IsInternational);

        var fileTokenRequest = new
        {
            name = ".save",
            __type = "File",
            ACL = new Dictionary<string, object>(),
            prefix = "gamesaves",
            metaData = new
            {
                size = packedSaveBuffer.Length,
                _checksum = Convert.ToHexString(MD5.HashData(packedSaveBuffer)),
                prefix = "gamesaves"
            }
        };
        fileTokenRequest.ACL["userObjectId"] = new
        {
            read = true,
            write = true
        };

        HttpResponseMessage response = await client.PostAsync(
            $"{baseUrl}/1.1/fileTokens",
            JsonContent.Create(fileTokenRequest, options: JsonOptions));
        response.EnsureSuccessStatusCode();

        FileTokenInfo? fileTokenInfo = await response.Content.ReadFromJsonAsync<FileTokenInfo>();
        return fileTokenInfo ?? throw new InvalidOperationException("Failed to parse file token response.");
    }

    private static async Task<CreateUploadResponse> CreateUpload(FileTokenInfo info, Save save)
    {
        HttpClient client = GetHttpClient(save);
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"https://upload.qiniup.com/buckets/rAK3Ffdi/objects/{Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key))}/uploads");
        request.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        CreateUploadResponse? createUploadResponse = await response.Content.ReadFromJsonAsync<CreateUploadResponse>();
        return createUploadResponse ?? throw new InvalidOperationException("Failed to create upload session.");
    }

    private static async Task<RequestUploadPart> UploadPart(
        int partNumber,
        byte[] packedSaveBuffer,
        FileTokenInfo info,
        CreateUploadResponse uploadCreation,
        Save save)
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

        RequestUploadPart? requestUploadPart = await response.Content.ReadFromJsonAsync<RequestUploadPart>();
        return requestUploadPart ?? throw new InvalidOperationException("Failed to upload save chunk.");
    }

    private static async Task CompleteUpload(
        FileTokenInfo info,
        CreateUploadResponse uploadCreation,
        Save save,
        params (int Index, RequestUploadPart Part)[] parts)
    {
        HttpClient client = GetHttpClient(save);
        HttpRequestMessage request1 = new(
            HttpMethod.Post,
            $"https://upload.qiniup.com/buckets/rAK3Ffdi/objects/{Convert.ToBase64String(Encoding.UTF8.GetBytes(info.key))}/uploads/{uploadCreation.uploadId}")
        {
            Content = JsonContent.Create(new
            {
                parts = parts.Select(x => new { partNumber = x.Index, x.Part.etag }).ToArray()
            }, options: JsonOptions)
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("UpToken", info.token);
        HttpResponseMessage response1 = await client.SendAsync(request1);
        response1.EnsureSuccessStatusCode();

        HttpRequestMessage request2 = new(HttpMethod.Post, $"{Save.GetCloudServerAddress(!save.IsInternational)}/1.1/fileCallback")
        {
            Content = JsonContent.Create(new
            {
                result = true,
                token = Convert.ToHexString(Encoding.UTF8.GetBytes(info.key))
            }, options: JsonOptions)
        };
        HttpResponseMessage response2 = await client.SendAsync(request2);
        response2.EnsureSuccessStatusCode();
    }

    private static async Task UpdateSummary(
        FileTokenInfo info,
        byte[] packedSummaryData,
        string? oldSaveObjectId,
        string userObjectId,
        Save save)
    {
        HttpClient client = GetHttpClient(save);
        string baseUrl = Save.GetCloudServerAddress(!save.IsInternational);

        var requestData = new
        {
            summary = Convert.ToBase64String(packedSummaryData),
            modifiedAt = new
            {
                __type = "Date",
                iso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.FFFZ", CultureInfo.InvariantCulture)
            },
            gameFile = new
            {
                __type = "Pointer",
                className = "_File",
                info.objectId
            },
            ACL = new Dictionary<string, object>(),
            user = new
            {
                __type = "Pointer",
                className = "_User",
                objectId = userObjectId
            }
        };
        requestData.ACL[userObjectId] = new
        {
            read = true,
            write = true,
        };

        string url = $"{baseUrl}/1.1/classes/_GameSave";
        HttpMethod method = HttpMethod.Put;
        if (!string.IsNullOrEmpty(oldSaveObjectId))
        {
            url += $"/{oldSaveObjectId}";
            method = HttpMethod.Post;
        }

        HttpRequestMessage request = new(method, url)
        {
            Content = JsonContent.Create(requestData, options: JsonOptions)
        };
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 删除旧的 game file 资源（如果需要）。
    /// </summary>
    /// <param name="oldSaveGameFileObjectId">待删除的 file objectId。</param>
    /// <param name="save">已初始化的 Save 实例。</param>
    public static async Task DeleteOld(string oldSaveGameFileObjectId, Save save)
    {
        HttpClient client = GetHttpClient(save);
        string baseUrl = Save.GetCloudServerAddress(!save.IsInternational);
        HttpResponseMessage response = await client.DeleteAsync($"{baseUrl}/1.1/files/{oldSaveGameFileObjectId}");
        response.EnsureSuccessStatusCode();
    }

#pragma warning restore IL2026, IL3050
}
