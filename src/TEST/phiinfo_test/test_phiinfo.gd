extends Control

@onready var label: Label = $VBoxContainer/Label
@onready var file_dialog: FileDialog = $FileDialog
@onready var line_edit: LineEdit = $VBoxContainer/LineEdit
@onready var progress_bar: ProgressBar = $VBoxContainer/ProgressBar

var phi_info_api: PhiInfoAPI
const CLDB_PATH: String = "res://addons/PhiInfo/classdata.tpk" # 需要事先准备好该文件

func _ready() -> void:
    label.text = "请点击按钮选择 Phigros APK 以初始化"
    
func _on_button_pressed() -> void:
    file_dialog.popup_centered()

func _on_web_button_pressed() -> void:
    var url = line_edit.text.strip_edges()
    if url == "":
        label.text = "请先输入有效的 APK 网络直链！"
        return
    _run_test(url, true)

func _on_file_dialog_file_selected(path: String) -> void:
    _run_test(path, false)

func _run_test(path_or_url: String, is_web: bool) -> void:
    var start = Time.get_ticks_usec()
    # 尝试调用刚才用 C# 编写的接口进行测试
    label.text = "分析中... 请稍候 (Web模式可能较慢)"
    progress_bar.value = 0.0
    await get_tree().process_frame
    
    var cldb_path = ProjectSettings.globalize_path(CLDB_PATH)
    if not FileAccess.file_exists(cldb_path):
        label.text = "错误: 缺少 cldb 文件：" + cldb_path
        return
        
    phi_info_api = PhiInfoAPI.new()
    phi_info_api.InitializationProgress.connect(_on_init_progress)
    
    if is_web:
        phi_info_api.InitFromSingleWebApkAsync(path_or_url, CLDB_PATH)
    else:
        phi_info_api.InitFromSingleApkAsync(path_or_url, CLDB_PATH)
    
    # 全局类可直接等待原生信号
    var result = await phi_info_api.InitializationCompleted
    var success = result[0]
    var error_msg = result[1]
    
    if not success:
        label.text = "初始化失败！\n" + error_msg
        return
        
    var test_output = "分析成功！(%s)\n" % ["Web模式" if is_web else "本地模式"]
    
    var version_info = phi_info_api.GetPhiVersion()
    test_output += "游戏版本: %s (Code: %s)\n" % [version_info["name"], version_info["code"]]
    
    var songs = phi_info_api.GetSongs()
    test_output += "共扫描到歌曲数量: %d\n" % songs.size()
    if songs.size() > 0:
        var first_song = songs[randi_range(0, len(songs)-1)]
        var song_id = first_song["id"]
        test_output += "随机歌曲 ID: %s \n" % song_id
        
        # 测试获取谱面 JSON
        if first_song.has("levels") and first_song["levels"].size() > 0:
            var first_level_idx = first_song["levels"].keys()[0] # 这里的 key 通常是 "0", "1" 等字符串
            var chart_json_str = phi_info_api.GetSongChart(song_id, first_level_idx.to_int())
            if chart_json_str:
                test_output += " >> 成功读取谱面文本 (Index:%s), 长度: %d 字符\n" % [first_level_idx, chart_json_str.length()]
                print(chart_json_str.substr(0,200))
            else:
                test_output += " >> 无法读取谱面文本 (Index:%s)\n" % first_level_idx

        # 测试获取音频
        var audio_stream = phi_info_api.GetSongMusic(song_id)
        if audio_stream:
            test_output += " >> 成功解析音频, 长度: %.1f 秒\n" % audio_stream.get_length()
            if has_node("AudioStreamPlayer"):
                var player = get_node("AudioStreamPlayer")
                player.stream = audio_stream
                player.play()
        else:
            test_output += " >> 无法读取音频\n"
                
        # 导出其他信息的统计
        var chapters = phi_info_api.GetChapters()
        var avatars = phi_info_api.GetAvatars()
        var tips = phi_info_api.GetTips()
        var collection = phi_info_api.GetCollection()
        var catalog = phi_info_api.GetAssetCatalogData()
        
        test_output += "\n--- 更多数据统计 ---\n"
        test_output += "章节数量: %d\n" % chapters.size()
        test_output += "头像数量: %d\n" % avatars.size()
        test_output += "Tips 数量: %d\n" % tips.size()
        test_output += "合集数量: %d\n" % collection.size()
        test_output += "资源目录项: %d\n" % catalog.size()
        
        # 测试获取图片
        var image = phi_info_api.GetSongIllustration(song_id)
        if image:
            $TextureRect.texture = ImageTexture.create_from_image(image)
            test_output += " >> 成功解析图片, %dx%d\n" % [image.get_width(), image.get_height()]
        else:
            test_output += " >> 无法读取曲绘图片\n"
        test_output += "用时 %f ms\n" % float((Time.get_ticks_usec()-start)/1000)
    
    label.text = test_output

func _on_init_progress(status: String, progress: float) -> void:
    label.text = status
    progress_bar.value = progress


func _on_audio_stream_player_finished() -> void:
    $AudioStreamPlayer.play()
