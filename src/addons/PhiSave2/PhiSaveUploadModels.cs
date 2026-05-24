using System;
using System.Collections.Generic;

namespace PhiStore.Addons.PhiSave2;

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
