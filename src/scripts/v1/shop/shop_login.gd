extends Control

var Config: ConfigFile
var sessiontoken: String
var tap_login: TapLogin
var QR_expire_time: int = 300
var Phi_Save: PhiSave = PhiSave.new($".")
var summary_data: Dictionary = {}
var logged: bool = false

signal on_sync_animation_finished

func _enter_tree() -> void:
    tap_login = TapLogin.new()
    add_child(tap_login)
    tap_login.device_flow_qr_ready.connect(_on_qr_ready)
    tap_login.cloud_user_created.connect(_on_login_successful)
    if OS.has_feature("web"):
        $LoginDialog/Login/TapTapLogin/Web.visible = false
        $LoginDialog/Login/TapTapLoginTips/Web.visible = false
    Config = ConfigFile.new()
    var load_result = Config.load("user://config.cfg")

    # 如果文件没有加载，忽略它。
    if load_result != OK or Config.get_valu
        $LoginDialog/QRLogin.visible = false
        $LoginDialog/SaveManage.visible = false
    else:
        logged = true

func _ready() -> void:
    if logged:
        set_user_info()

func set_user_info():
    var load_result = Config.load("user://config.cfg")

    # 如果文件没有加载，忽略它。
    if load_result != OK or Config.get_value("Config", "sessionToken", "") == "":
        # 准备登录
        push_warning("config.cfg 加载失败")
    else:
        sessiontoken = Config.get_value("Config", "sessionToken")
        # summary_data = await Phi_Save.get_cloud_save_summary()
        summary_data = PhiSaveTools.generate_summary()
        $LoginDialog/SaveManage/VBoxContainer/HBoxContainer/Player.set("text", "Player: %s" % Config.get_value("Config", "nickName", "Phigros-Null"))
        $LoginDialog/SaveManage/VBoxContainer/HBoxContainer/ID.set("text", "ID: %s" % Config.get_value("Config", "shortId", "Session"))
        $LoginDialog/SaveManage/VBoxContainer/HBoxContainer/Rks.set("text", "RankingScore: %.2f" % summary_data["rks"])
        $LoginDialog/SaveManage/VBoxContainer/HBoxContainer2/LastLocalUpdate.set("text", "上次下载： %s" % Time.get_datetime_string_from_unix_time(Config.get_value("SaveInfo", "updateTime", 0) + 28800, true).replace("-", "/"))
        $LoginDialog/SaveManage/VBoxContainer/HBoxContainer2/LastCloudUpdate.set("text", "云端存档： %s" % Time.get_datetime_string_from_unix_time(await Phi_Save.get_cloud_save_update_time() + 28800, true).replace("-", "/"))
        $LoginDialog/SaveManage/Avatar.set("texture", load(PhiSaveTools.get_avatar_path(summary_data["avatar"])))

func _on_bg_button_pressed() -> void:
    $AnimationPlayer.play_backwards(&"in")
    await $AnimationPlayer.animation_finished
    if self.is_inside_tree():
        self.queue_free()

func _on_qr_login_pressed() -> void:
    $AnimationPlayer.play_backwards(&"in")
    await $AnimationPlayer.animation_finished
    $LoginDialog/Login.visible = false
    $LoginDialog/QRLogin.visible = true
    $LoginDialog/SaveManage.visible = false
    tap_login.start_QR_code_flow()

func _on_qr_ready(qr_url: String) -> void:
    $LoginDialog/QRLogin/ExpiredTime.set(&"theme_override_colors/font_color", Color.WHITE)
    $LoginDialog/QRLogin/QRCodeRect.data = qr_url
    $AnimationPlayer.play(&"in")
    QR_expire_time = 300
    $LoginDialog/QRLogin/QRCodeRect/Expired.visible = false
    while QR_expire_time > 0:
        $LoginDialog/QRLogin/ExpiredTime.text = "剩余 %d s" % QR_expire_time
        await get_tree().create_timer(1).timeout
        QR_expire_time -= 1
    if QR_expire_time <= 0:
        push_warning("二维码已过期")
        $LoginDialog/QRLogin/ExpiredTime.text = "剩余 %d s" % 0
        $LoginDialog/QRLogin/ExpiredTime.set(&"theme_override_colors/font_color", Color.TOMATO)
        $LoginDialog/QRLogin/QRCodeRect/Expired.visible = true


func _on_retry_pressed() -> void:
    tap_login.start_QR_code_flow()

func _on_login_successful(cloud_data: Dictionary):
    print("\nCloud user Getted: ", cloud_data)
    print("\nSessiontoken: ", cloud_data["sessionToken"])
    _sync_save_start()
    await on_sync_animation_finished
    $SyncTimer.start()
    Phi_Save.SessionToken_login(cloud_data["sessionToken"], cloud_data)
    await Phi_Save.on_sessiontoken_sync_success
    $SyncTimer.stop()
    _sync_save_end(true)
    #_on_bg_button_pressed()


func _on_web_login_pressed() -> void:
    tap_login.start_browser_auth_flow()


func _on_upload() -> void:
    _sync_save_start()
    await on_sync_animation_finished
    $SyncTimer.start()
    await Phi_Save.SessionToken_upload_save()
    await Phi_Save.on_sessiontoken_upload_success
    $SyncTimer.stop()
    _sync_save_end()
    set_user_info()

func _on_get() -> void:
    _sync_save_start()
    await on_sync_animation_finished
    $SyncTimer.start()
    await Phi_Save.SessionToken_get_save()
    await Phi_Save.on_sessiontoken_get_success
    $SyncTimer.stop()
    _sync_save_end(true)
    set_user_info()

func _on_logout() -> void:
    Phi_Save.SessionToken_logout()
    logged = false
    _on_bg_button_pressed()


func _on_sessiontoken_ok_pressed() -> void:
    if $LoginDialog/Login/SessionToken.text != "":
        Phi_Save.SessionToken_login($LoginDialog/Login/SessionToken.text)
        await Phi_Save.on_sessiontoken_sync_success
        _on_bg_button_pressed()
    else:
        push_warning("请输入SessionToken")
        $LoginDialog/Login/TapTap.text = "请输入SessionToken！"
        $LoginDialog/Login/TapTap.set(&"theme_override_colors/font_color", Color.TOMATO)
        await get_tree().create_timer(3).timeout
        $LoginDialog/Login/TapTap.text = "TapTap 登录"
        $LoginDialog/Login/TapTap.set(&"theme_override_colors/font_color", Color.WHITE)

func _sync_save_start() -> void:
    $AnimationPlayer.play_backwards(&"in")
    await $AnimationPlayer.animation_finished
    $Syncing.visible = true
    $AnimationPlayer.play(&"sync_dialog_in")
    await $AnimationPlayer.animation_finished
    $LoginDialog.visible = false
    $AnimationPlayer.play(&"indicate_loop")
    on_sync_animation_finished.emit()

func _sync_save_end(restart: bool = false) -> void:
    $AnimationPlayer.stop()
    $Syncing/Msg.text = "同步成功！\n 即将于3s后重启..." if restart else "同步成功！"
    await get_tree().create_timer(3).timeout
    $AnimationPlayer.play_backwards(&"sync_dialog_in")
    await $AnimationPlayer.animation_finished
    $Syncing.visible = false
    if restart:
        await $"/root/Shop/TransitionManager".transition_to("res://scenes/global/splash0.tscn")
    else:
        $LoginDialog.visible = true
        $AnimationPlayer.play(&"in")

func _sync_save_error(error_msg: String) -> void:
    $AnimationPlayer.stop()
    $Syncing/Msg.text = error_msg
    $Syncing/Msg.set(&"theme_override_colors/font_color", Color.TOMATO)
    await get_tree().create_timer(2).timeout
    $AnimationPlayer.play_backwards(&"sync_dialog_in")
    await $AnimationPlayer.animation_finished
    $Syncing.visible = false
    $LoginDialog.visible = true
    $AnimationPlayer.play(&"in")

func _on_sync_timer_timeout() -> void:
    _sync_save_error("错误：同步超时！")
