using System.Collections.Generic;
using System.Text.Json.Serialization;
using PhigrosLibraryCSharp.CloudSave;
using PhigrosLibraryCSharp.CloudSave.Login;
using PhiStore.Addons.PhiSave2.Models;

namespace PhiStore.Addons.PhiSave2;

[JsonSerializable(typeof(PhiSaveData))]
[JsonSerializable(typeof(PhiSaveUploader.FileTokenInfo))]
[JsonSerializable(typeof(PhiSaveUploader.CreateUploadResponse))]
[JsonSerializable(typeof(PhiSaveUploader.RequestUploadPart))]
[JsonSerializable(typeof(PhiSaveUploader.FileTokenRequest))]
[JsonSerializable(typeof(PhiSaveUploader.AclPermission))]
[JsonSerializable(typeof(PhiSaveUploader.CompleteUploadRequest))]
[JsonSerializable(typeof(PhiSaveUploader.FileCallbackRequest))]
[JsonSerializable(typeof(PhiSaveUploader.UpdateSummaryRequest))]
[JsonSerializable(typeof(TapTapTokenData))]
[JsonSerializable(typeof(SongScore))]
[JsonSerializable(typeof(List<SongScore>))]
[JsonSerializable(typeof(GameRecord))]
[JsonSerializable(typeof(GameProgress))]
[JsonSerializable(typeof(GameSettings))]
[JsonSerializable(typeof(GameUserInfo))]
[JsonSerializable(typeof(GameKeyFlag))]
[JsonSerializable(typeof(Challenge))]
[JsonSerializable(typeof(Money))]
[JsonSerializable(typeof(GameProgressNodeVersion2))]
[JsonSerializable(typeof(GameProgressNodeVersion3))]
[JsonSerializable(typeof(GameProgressNodeVersion4))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(Dictionary<string, object>), TypeInfoPropertyName = "DictionaryStringObject")]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "DictionaryStringString")]
public partial class PhiSaveJsonContext : JsonSerializerContext
{
}
