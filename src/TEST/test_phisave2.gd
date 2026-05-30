extends Control

# UI Components
@onready var main_panel: ScrollContainer = $MainPanel
@onready var status_label: Label = $MainPanel/VBoxContainer/StatusLabel
@onready var log_text: TextEdit = $MainPanel/VBoxContainer/ScrollContainer/LogText
@onready var login_method_container: VBoxContainer = $MainPanel/VBoxContainer/LoginContainer
@onready var qr_code_panel: Panel = $MainPanel/VBoxContainer/QRCodePanel
@onready var qr_code_rect: TextureRect = $MainPanel/VBoxContainer/QRCodePanel/QRCodeContainer/QRCodeRect
@onready var oauth_link_button: Button = $MainPanel/VBoxContainer/OAuthLinkButton
@onready var conflict_resolution_panel: Panel = $MainPanel/VBoxContainer/ConflictPanel
@onready var diff_list: ItemList = $MainPanel/VBoxContainer/ConflictPanel/VBoxContainer/DiffList
@onready var conflict_description: Label = $MainPanel/VBoxContainer/ConflictPanel/VBoxContainer/ConflictDescription
@onready var merge_button: Button = $MainPanel/VBoxContainer/ConflictPanel/VBoxContainer/ButtonHBox/MergeButton
@onready var keep_local_button: Button = $MainPanel/VBoxContainer/ConflictPanel/VBoxContainer/ButtonHBox/KeepLocalButton
@onready var keep_cloud_button: Button = $MainPanel/VBoxContainer/ConflictPanel/VBoxContainer/ButtonHBox/KeepCloudButton

# Login UI
@onready var qr_login_button: Button = $MainPanel/VBoxContainer/LoginContainer/QRLoginButton
@onready var oauth_login_button: Button = $MainPanel/VBoxContainer/LoginContainer/OAuthLoginButton
@onready var init_token_button: Button = $MainPanel/VBoxContainer/LoginContainer/TokenInputContainer/InitTokenButton
@onready var token_input: LineEdit = $MainPanel/VBoxContainer/LoginContainer/TokenInputContainer/TokenInput
@onready var session_token_display: Label = $MainPanel/VBoxContainer/SessionTokenLabel

# Download & Manage
@onready var download_button: Button = $MainPanel/VBoxContainer/DownloadButton
@onready var check_conflict_button: Button = $MainPanel/VBoxContainer/CheckConflictButton
@onready var save_local_button: Button = $MainPanel/VBoxContainer/SaveLocalButton
@onready var load_local_button: Button = $MainPanel/VBoxContainer/LoadLocalButton
@onready var upload_button: Button = $MainPanel/VBoxContainer/UploadButton
@onready var conflict_upload_button: Button = $MainPanel/VBoxContainer/ConflictUploadButton
@onready var export_json_button: Button = $MainPanel/VBoxContainer/ExportJsonButton
@onready var import_json_button: Button = $MainPanel/VBoxContainer/ImportJsonButton
@onready var calculate_rks_button: Button = $MainPanel/VBoxContainer/CalculateRKSButton
@onready var aes_key_input: LineEdit = $MainPanel/VBoxContainer/AesKeyContainer/AesKeyInput
@onready var aes_iv_input: LineEdit = $MainPanel/VBoxContainer/AesIvContainer/AesIvInput
@onready var random_crypto_button: Button = $MainPanel/VBoxContainer/RandomCryptoButton

# Service & Data
var phi_save2_api: PhiSave2API
var phi_info_api: PhiInfoAPI
var current_difficulties: Dictionary = {}
var local_save_path: String = "user://phi_save_local.enc"
var encryption_key: PackedByteArray
var encryption_iv: PackedByteArray
const CLDB_PATH: String = "res://addons/PhiInfo/classdata.tpk"

# OAuth Configuration (example values, should be updated with real credentials)
var oauth_port: int = 8080
var oauth_auth_endpoint: String = "https://accounts.taptap.com/authorize"
var oauth_token_endpoint: String = "https://accounts.tapapis.cn/oauth2/v1/token"
var oauth_client_id: String = "rAK3FfdieFob2Nn8Am"
var oauth_client_secret: String = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0"

# State tracking
var is_logged_in: bool = false
var has_save_downloaded: bool = false
var current_diff_list: Array = []
var old_save_game_file_object_id: String = ""
var old_save_object_id: String = ""
var _pending_requests: Array = []

func _ready() -> void:
    phi_save2_api = PhiSave2API.new()

    _set_random_crypto_inputs()
    
    # Connect all signals
    _connect_signals()
    
    # Initialize UI state
    _update_ui_state()
    
    _log("PhiSave2 Test Scene initialized")
    _log("Generated encryption keys for local persistence")

func _connect_signals() -> void:
    # Login signals
    phi_save2_api.QrCodeGenerated.connect(_on_qr_code_generated)
    phi_save2_api.LoginSuccess.connect(_on_login_success)
    phi_save2_api.LoginFailed.connect(_on_login_failed)
    phi_save2_api.OAuthUrlGenerated.connect(_on_oauth_url_generated)
    phi_save2_api.OAuthLoginResult.connect(_on_oauth_login_result)
    
    # Button signals
    qr_login_button.pressed.connect(_on_qr_login_pressed)
    oauth_login_button.pressed.connect(_on_oauth_login_pressed)
    init_token_button.pressed.connect(_on_init_token_pressed)
    download_button.pressed.connect(_on_download_pressed)
    check_conflict_button.pressed.connect(_on_check_conflict_pressed)
    save_local_button.pressed.connect(_on_save_local_pressed)
    load_local_button.pressed.connect(_on_load_local_pressed)
    upload_button.pressed.connect(_on_upload_pressed)
    conflict_upload_button.pressed.connect(_on_conflict_upload_pressed)
    export_json_button.pressed.connect(_on_export_json_pressed)
    import_json_button.pressed.connect(_on_import_json_pressed)
    calculate_rks_button.pressed.connect(_on_calculate_rks_pressed)
    random_crypto_button.pressed.connect(_on_random_crypto_pressed)
    
    # Conflict resolution buttons
    merge_button.pressed.connect(_on_merge_pressed)
    keep_local_button.pressed.connect(_on_keep_local_pressed)
    keep_cloud_button.pressed.connect(_on_keep_cloud_pressed)

func _on_qr_login_pressed() -> void:
    _log("Starting QR Code login...")
    status_label.text = "QR Code login in progress..."
    qr_code_panel.visible = true
    login_method_container.visible = false
    phi_save2_api.StartQrLogin()

func _on_oauth_login_pressed() -> void:
    _log("Starting OAuth login...")
    status_label.text = "OAuth login in progress..."
    login_method_container.visible = false
    # Pass 0 to automatically select an available port
    phi_save2_api.StartOAuthLogin(0, oauth_auth_endpoint, oauth_token_endpoint,
                                    oauth_client_id, oauth_client_secret, "")

func _on_init_token_pressed() -> void:
    var token = token_input.text.strip_edges()
    if token.is_empty():
        _log("ERROR: Token input is empty!")
        return
    _log("Initializing with session token...")
    phi_save2_api.InitWithSessionToken(token,oauth_client_id,oauth_client_secret)
    is_logged_in = true
    _on_login_success(token, "unknown")

func _on_qr_code_generated(url: String, expires_in_seconds: int) -> void:
    _log("QR Code generated (expires in %d seconds)" % expires_in_seconds)
    _log("URL: %s" % url)
    
    # Use QRCodeRect to display the QR code
    # QRCodeRect accepts URL data directly via the 'data' property
    qr_code_rect.data = url
    qr_code_rect.auto_version = true
    qr_code_rect.mode = qr_code_rect._qr.Mode.BYTE
    qr_code_rect.light_module_color = Color.WHITE
    qr_code_rect.dark_module_color = Color.BLACK

func _on_login_success(session_token: String, user_object_id: String) -> void:
    _log("✓ Login successful!")
    _log("  Session Token: %s" % session_token)
    _log("  User Object ID: %s" % user_object_id)
    
    is_logged_in = true
    session_token_display.text = "Logged in (Token: %s...)" % session_token.substr(0, 20)
    status_label.text = "Logged in successfully"
    qr_code_panel.visible = false
    login_method_container.visible = false
    
    _update_ui_state()
    
    # Load PhiInfo for difficulty data
    _load_phi_info()

func _on_login_failed(error: String) -> void:
    _log("✗ Login failed: %s" % error)
    status_label.text = "Login failed: %s" % error
    qr_code_panel.visible = false
    login_method_container.visible = true
    _update_ui_state()

func _on_oauth_url_generated(url: String) -> void:
    _log("OAuth URL generated:")
    _log(url)
    status_label.text = "Opening OAuth URL in browser..."
    oauth_link_button.text = "Open OAuth Link"
    oauth_link_button.visible = true
    
    # Disconnect any previous connections
    if oauth_link_button.pressed.is_connected(func(): pass ):
        for sig in oauth_link_button.pressed.get_connections():
            oauth_link_button.pressed.disconnect(sig.callable)
    
    # Connect new callback
    var open_url_callback = func():
        OS.shell_open(url)
        _log("Browser opening URL: %s" % url)
    oauth_link_button.pressed.connect(open_url_callback)

func _on_oauth_login_result(success: bool, token_or_error: String) -> void:
    if success:
        _log("OAuth login flow completed (session token received).")
        status_label.text = "OAuth login complete"
    else:
        _on_login_failed(token_or_error)

func _on_download_pressed() -> void:
    if not is_logged_in:
        _log("ERROR: Not logged in!")
        return
    
    _log("Downloading save from cloud...")
    status_label.text = "Downloading save..."
    
    var request = phi_save2_api.DownloadAndDecryptSaveAsync()
    _hold_request(request)
    request.Completed.connect(func(result):
        var file_id = result.get("oldSaveGameFileObjectId", "")
        var obj_id = result.get("oldSaveObjectId", "")
        old_save_game_file_object_id = file_id
        old_save_object_id = obj_id
        _log("✓ Save downloaded successfully")
        _log("  File ID: %s" % file_id)
        _log("  Object ID: %s" % obj_id)
        has_save_downloaded = true
        status_label.text = "Save downloaded"
        _update_ui_state()
        _release_request(request)
    )
    request.Error.connect(func(error):
        _log("✗ Download failed: %s" % error)
        status_label.text = "Download failed: %s" % error
        _release_request(request)
    )

func _on_check_conflict_pressed() -> void:
    if not has_save_downloaded:
        _log("ERROR: No save downloaded yet!")
        return
    
    _log("Checking for conflicts...")
    status_label.text = "Checking conflicts..."
    
    var metadata_request = phi_save2_api.GetSaveMetadata(local_save_path)
    _hold_request(metadata_request)
    metadata_request.Completed.connect(func(metadata):
        _log("Conflict metadata retrieved:")
        _log("  Local modified: %s" % metadata.get("local_modified_utc", "N/A"))
        _log("  Cloud modified: %s" % metadata.get("cloud_modified_utc", "N/A"))
        _log("  Local RKS: %.2f" % metadata.get("local_rks", 0.0))
        _log("  Cloud RKS: %.2f" % metadata.get("cloud_rks", 0.0))
        
        var local_time = metadata.get("local_modified_utc", "")
        var cloud_time = metadata.get("cloud_modified_utc", "")
        
        if local_time and cloud_time and local_time != cloud_time:
            _log("CONFLICT DETECTED: Local and cloud saves differ!")
            _show_conflict_resolution()
        else:
            _log("No conflict detected")
            status_label.text = "No conflict"
        _release_request(metadata_request)
    )
    metadata_request.Error.connect(func(error):
        _log("✗ Metadata retrieval failed: %s" % error)
        status_label.text = "Conflict check failed: %s" % error
        _release_request(metadata_request)
    )

func _show_conflict_resolution() -> void:
    _log("Fetching differences...")
    
    var diff_request = phi_save2_api.DiffWithCloud()
    _hold_request(diff_request)
    diff_request.Completed.connect(func(result: Dictionary):
        var diffs: Array = result.get("score_diffs", [])
        var full_diffs: Array = result.get("full_diffs", [])
        var local_sum: Dictionary = result.get("local_summary", {})
        var cloud_sum: Dictionary = result.get("cloud_summary", {})
        
        current_diff_list = diffs
        diff_list.clear()

        var conflict_text = "Found %d differences:\n" % diffs.size()
        conflict_text += "Local Summary -> GameVer: %s | Rks: %.4f | Avatar: %s\n" % [local_sum.get("gameVersion", "0"), local_sum.get("rks", 0.0), local_sum.get("avatar", "")]
        conflict_text += "Cloud Summary -> GameVer: %s | Rks: %.4f | Avatar: %s\n\n" % [cloud_sum.get("gameVersion", "0"), cloud_sum.get("rks", 0.0), cloud_sum.get("avatar", "")]

        if full_diffs.size() > 0:
            conflict_text += "Full Save Differences: %d items\n" % full_diffs.size()
            for fd in full_diffs:
                conflict_text += "  [%s] %s\n" % [fd.get("type", "unknown"), fd.get("path", "")]
            conflict_text += "\n"
        
        for diff in diffs:
            var song_id = diff.get("songId", "unknown")
            var difficulty = diff.get("difficulty", -1)
            var local_score = diff.get("local_score", -1)
            var cloud_score = diff.get("cloud_score", -1)

            var label = "%s [Diff %d] Local: %d vs Cloud: %d" % [song_id, difficulty, local_score, cloud_score]
            diff_list.add_item(label)
            conflict_text += "  %s\n" % label

        conflict_description.text = conflict_text
        conflict_resolution_panel.visible = true
        status_label.text = "Conflict resolution required"
        _log("✓ Conflicts displayed, awaiting user choice")
        _release_request(diff_request)
    )
    diff_request.Error.connect(func(error):
        _log("✗ Diff retrieval failed: %s" % error)
        status_label.text = "Diff check failed: %s" % error
        _release_request(diff_request)
    )

func _on_merge_pressed() -> void:
    _log("Executing merge strategy...")
    status_label.text = "Merging saves..."
    
    var merge_request = phi_save2_api.MergeWithCloudToLocal(local_save_path)
    _hold_request(merge_request)
    merge_request.Completed.connect(func(_result):
        _log("✓ Saves merged successfully")
        status_label.text = "Merge complete"
        conflict_resolution_panel.visible = false
        _update_ui_state()
        _release_request(merge_request)
    )
    merge_request.Error.connect(func(error):
        _log("✗ Merge failed: %s" % error)
        status_label.text = "Merge failed: %s" % error
        _release_request(merge_request)
    )

func _on_keep_local_pressed() -> void:
    _log("Keeping local save and uploading to cloud...")
    status_label.text = "Uploading local save..."
    
    var upload_request = phi_save2_api.UploadSaveAsync(old_save_game_file_object_id, old_save_object_id)
    _hold_request(upload_request)
    upload_request.Completed.connect(func(_result):
        _log("✓ Local save uploaded successfully")
        status_label.text = "Upload complete"
        conflict_resolution_panel.visible = false
        _update_ui_state()
        _release_request(upload_request)
    )
    upload_request.Error.connect(func(error):
        _log("✗ Upload failed: %s" % error)
        status_label.text = "Upload failed: %s" % error
        _release_request(upload_request)
    )

func _on_keep_cloud_pressed() -> void:
    _log("Keeping cloud save and downloading to local...")
    status_label.text = "Downloading cloud save..."
    
    var download_request = phi_save2_api.DownloadAndDecryptSaveAsync()
    _hold_request(download_request)
    download_request.Completed.connect(func(_result):
        _log("✓ Cloud save downloaded successfully")
        status_label.text = "Download complete"
        conflict_resolution_panel.visible = false
        _update_ui_state()
        _release_request(download_request)
    )
    download_request.Error.connect(func(error):
        _log("✗ Download failed: %s" % error)
        status_label.text = "Download failed: %s" % error
        _release_request(download_request)
    )

func _on_save_local_pressed() -> void:
    if not phi_save2_api.HasCurrentSave():
        _log("ERROR: No save in memory!")
        return
    
    _log("Saving current save to local encrypted file...")
    _update_crypto_from_inputs()
    var result = phi_save2_api.SaveToLocal(local_save_path, encryption_key, encryption_iv)
    
    if result == "OK":
        _log("✓ Save persisted to: %s" % local_save_path)
        status_label.text = "Local save complete"
    else:
        _log("✗ Save failed: %s" % result)
        status_label.text = "Save failed: %s" % result

func _on_load_local_pressed() -> void:
    _log("Loading save from local encrypted file...")
    _update_crypto_from_inputs()
    var result = phi_save2_api.LoadFromLocal(local_save_path, encryption_key, encryption_iv)
    
    if result == "OK":
        _log("✓ Save loaded from: %s" % local_save_path)
        status_label.text = "Local load complete"
        _update_ui_state()
    else:
        _log("✗ Load failed: %s" % result)
        status_label.text = "Load failed: %s" % result

func _on_upload_pressed() -> void:
    if not has_save_downloaded:
        _log("ERROR: Please download a cloud save first to obtain old object ids.")
        return

    _log("Uploading current save to cloud...")
    status_label.text = "Uploading save..."

    var upload_request = phi_save2_api.UploadSaveAsync(old_save_game_file_object_id, old_save_object_id)
    _hold_request(upload_request)
    upload_request.Completed.connect(func(_result):
        _log("✓ Save uploaded successfully")
        status_label.text = "Upload complete"
        _update_ui_state()
        _release_request(upload_request)
    )
    upload_request.Error.connect(func(error):
        _log("✗ Upload failed: %s" % error)
        status_label.text = "Upload failed: %s" % error
        _release_request(upload_request)
    )

func _on_conflict_upload_pressed() -> void:
    if not has_save_downloaded:
        _log("ERROR: Please download a save first before testing conflict upload.")
        return

    _log("Testing conflict upload path (keep local and upload)...")
    status_label.text = "Testing conflict upload..."

    var upload_request = phi_save2_api.UploadSaveAsync(old_save_game_file_object_id, old_save_object_id)
    _hold_request(upload_request)
    upload_request.Completed.connect(func(_result):
        _log("✓ Conflict upload path completed successfully")
        status_label.text = "Conflict upload complete"
        conflict_resolution_panel.visible = false
        _update_ui_state()
        _release_request(upload_request)
    )
    upload_request.Error.connect(func(error):
        _log("✗ Conflict upload failed: %s" % error)
        status_label.text = "Conflict upload failed: %s" % error
        _release_request(upload_request)
    )

func _on_export_json_pressed() -> void:
    if not phi_save2_api.HasCurrentSave():
        _log("ERROR: No save in memory!")
        return
    
    _log("Exporting save as JSON...")
    var export_path = "user://phi_save_export.json"
    var result = phi_save2_api.ExportJsonToFile(export_path)
    
    if result == "OK":
        _log("✓ JSON exported to: %s" % export_path)
        _log("  Unicode 已按明文导出（非 \\uXXXX）")
        status_label.text = "JSON exported to file"
        
        # Also print sample to log
        var json_str = phi_save2_api.ExportJson()
        _log("JSON Preview (first 200 chars): " + json_str.substr(0, 200) + "...")
    else:
        _log("✗ Export failed: %s" % result)

func _on_import_json_pressed() -> void:
    var import_path = "user://phi_save_export.json"
    _log("Importing JSON save from: %s" % import_path)
    var result = phi_save2_api.ImportJsonFromFile(import_path)
    if result == "OK":
        _log("✓ JSON import successful")
        status_label.text = "JSON import complete"
        if current_difficulties.is_empty():
            _log("WARNING: Difficulty data is not loaded yet, local RKS cannot be recalculated.")
        else:
            var recalculated_rks = phi_save2_api.CalculateInMemoryRks(current_difficulties)
            _log("✓ Local RKS recalculated after import: %.2f" % recalculated_rks)
        _update_ui_state()
    else:
        _log("✗ JSON import failed: %s" % result)
        status_label.text = "JSON import failed"

func _on_random_crypto_pressed() -> void:
    _set_random_crypto_inputs()

func _set_random_crypto_inputs() -> void:
    var keys = phi_save2_api.GenerateLocalKeys()
    var key_ba := PackedByteArray(keys[0])
    var iv_ba := PackedByteArray(keys[1])
    encryption_key = key_ba
    encryption_iv = iv_ba
    aes_key_input.text = key_ba.hex_encode()
    aes_iv_input.text = iv_ba.hex_encode()
    _log("Encryption Key: " + aes_key_input.text + "\nIV: " + aes_iv_input.text)

func _update_crypto_from_inputs() -> void:
    var key_hex = aes_key_input.text.strip_edges()
    var iv_hex = aes_iv_input.text.strip_edges()

    var parsed_key = _hex_to_bytes(key_hex)
    var parsed_iv = _hex_to_bytes(iv_hex)

    if parsed_key.size() == 32:
        encryption_key = parsed_key
    else:
        _log("WARNING: AES key must be 32 bytes (64 hex chars). Keep previous key.")

    if parsed_iv.size() == 16:
        encryption_iv = parsed_iv
    else:
        _log("WARNING: AES iv must be 16 bytes (32 hex chars). Keep previous iv.")

func _hex_to_bytes(hex_text: String) -> PackedByteArray:
    var s = hex_text.replace(" ", "").replace("\n", "").replace("\t", "")
    if s.length() % 2 != 0:
        return PackedByteArray()

    var out := PackedByteArray()
    for i in range(0, s.length(), 2):
        var pair = s.substr(i, 2)
        if not pair.is_valid_hex_number(false):
            return PackedByteArray()
        out.append(pair.hex_to_int())
    return out

func _on_calculate_rks_pressed() -> void:
    if not phi_save2_api.HasCurrentSave():
        _log("ERROR: No save in memory!")
        return
    
    if current_difficulties.is_empty():
        _log("WARNING: No difficulty data loaded, RKS calculation may be incomplete")
    
    _log("Calculating RKS with current difficulty data...")
    var rks = phi_save2_api.CalculateInMemoryRks(current_difficulties)
    _log("✓ RKS calculated: %.2f" % rks)
    status_label.text = "RKS: %.2f" % rks

func _load_phi_info() -> void:
    _log("Loading PhiInfo data for difficulty constants...")
    
    if not phi_info_api:
        phi_info_api = PhiInfoAPI.new()
    
    # Try to load from installed APK first, otherwise fetch TapTap web link in C# and use it
    var apk_path = "user://com.PigeonGames.Phigros-127.apk"
    var use_web_apk = not FileAccess.file_exists(apk_path)
    if use_web_apk:
        _log("No local APK found, resolving TapTap download URL via C# helper...")
    
    # Create a temporary task to handle async loading
    var task = func():
        var init_source = apk_path
        if use_web_apk:
            var apk_request = phi_save2_api.GetTapTapApkLinkAsync(165287)
            _hold_request(apk_request)
            var apk_result = await apk_request.Completed
            _release_request(apk_request)
            
            # apk_result is directly a Dictionary, not an Array
            var apk_data: Dictionary = apk_result
            var apk_url = apk_data.get("download_url", "")
            var apk_file_name = apk_data.get("file_name", "")
            _log("✓ TapTap APK URL resolved")
            _log("  File Name: %s" % apk_file_name)
            _log("  URL: %s" % apk_url)
            init_source = apk_url
        else:
            _log("Initializing PhiInfo from local APK: %s" % apk_path)

        if use_web_apk:
            phi_info_api.InitFromSingleWebApkAsync(init_source, CLDB_PATH)
        else:
            phi_info_api.InitFromSingleApkAsync(init_source, CLDB_PATH)
        
        var result = await phi_info_api.InitializationCompleted
        if not result[0]:
            _log("WARNING: PhiInfo initialization failed: %s" % result[1])
            return
        
        _log("✓ PhiInfo loaded successfully")
        
        # Load songs and build difficulty map
        var songs = await _call_phi_info_async(phi_info_api.GetSongsAsync)
        if songs:
            _log("Loading difficulties for %d songs..." % songs.size())
            for song in songs:
                var song_id = song.get("id", "")
                if not song.has("levels"):
                    continue
                
                var levels = song["levels"]
                for idx in levels.keys():
                    var level = levels[idx]
                    var difficulty = level.get("difficulty", 0.0)
                    var idx_num = _normalize_diff_idx(idx)
                    var key = "%s_%d" % [song_id, idx_num]
                    current_difficulties[key] = difficulty
            
            _log("✓ Loaded %d difficulty entries" % current_difficulties.size())
    
    # Execute the task and await completion
    await task.call()

func _update_ui_state() -> void:
    login_method_container.visible = not is_logged_in
    download_button.visible = is_logged_in
    check_conflict_button.visible = is_logged_in and has_save_downloaded
    save_local_button.visible = is_logged_in
    load_local_button.visible = is_logged_in
    upload_button.visible = is_logged_in and phi_save2_api.HasCurrentSave()
    conflict_upload_button.visible = is_logged_in and phi_save2_api.HasCurrentSave()
    export_json_button.visible = is_logged_in
    import_json_button.visible = is_logged_in
    calculate_rks_button.visible = is_logged_in
    save_local_button.disabled = not phi_save2_api.HasCurrentSave()
    export_json_button.disabled = not phi_save2_api.HasCurrentSave()
    calculate_rks_button.disabled = not phi_save2_api.HasCurrentSave()
    upload_button.disabled = not phi_save2_api.HasCurrentSave()
    conflict_upload_button.disabled = not phi_save2_api.HasCurrentSave()
    check_conflict_button.disabled = not has_save_downloaded
    conflict_resolution_panel.visible = false

func _log(message: String) -> void:
    var timestamp = Time.get_ticks_msec()
    var formatted = "[%d] %s" % [timestamp, message]
    log_text.text += formatted + "\n"
    print(formatted)
    #log_text.scroll_to_line(log_text.get_line_count())

func _hold_request(request: RefCounted) -> void:
    if request == null:
        return
    _pending_requests.append(request)

func _release_request(request: RefCounted) -> void:
    if request == null:
        return
    _pending_requests.erase(request)

## 简洁的 PhiInfo 异步请求包装：隐藏 Callable 包装，直接返回资源结果
func _call_phi_info_async(request_callable: Callable) -> Variant:
    var request = request_callable.call()
    var result = await request.Completed
    return result

func _normalize_diff_idx(raw_idx: Variant) -> int:
    if typeof(raw_idx) == TYPE_INT:
        return raw_idx
    var s = str(raw_idx).to_upper()
    match s:
        "EZ", "EASY":
            return 0
        "HD", "HARD":
            return 1
        "IN", "INSANE":
            return 2
        "AT", "ANOTHER":
            return 3
        "LEGACY":
            return 4
        _:
            return int(raw_idx)
