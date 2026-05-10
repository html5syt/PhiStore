using System.Text.Json.Serialization;
using PhiStore.Addons.PhiSave2.Models;

namespace PhiStore.Addons.PhiSave2;

[JsonSerializable(typeof(PhiSaveData))]
[JsonSerializable(typeof(PhiSaveUploader.FileTokenInfo))]
[JsonSerializable(typeof(PhiSaveUploader.CreateUploadResponse))]
[JsonSerializable(typeof(PhiSaveUploader.RequestUploadPart))]
[JsonSerializable(typeof(PhiSaveUploader.FileTokenRequest))]
[JsonSerializable(typeof(PhiSaveUploader.CompleteUploadRequest))]
[JsonSerializable(typeof(PhiSaveUploader.FileCallbackRequest))]
[JsonSerializable(typeof(PhiSaveUploader.UpdateSummaryRequest))]
public partial class PhiSaveJsonContext : JsonSerializerContext
{
}
