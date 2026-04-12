extends Control

@onready var label: Label = $VBoxContainer/Label
@onready var file_dialog: FileDialog = $FileDialog
@onready var line_edit: LineEdit = $VBoxContainer/LineEdit
@onready var progress_bar: ProgressBar = $VBoxContainer/ProgressBar

var phi_info_api: PhiInfoAPI = null
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
    
    var songs_json = phi_info_api.GetSongsJson()
    var test_json = JSON.new()
    var error = test_json.parse(songs_json)
    if error == OK:
        var songs = test_json.data
        test_output += "共扫描到歌曲数量: %d\n" % songs.size()
        if songs.size() > 0:
            var first_song = songs[0]
            var song_id = first_song["id"]
            test_output += "首个歌曲 ID: %s \n" % song_id
            
            # 测试获取谱面 JSON
            if first_song.has("levels") and first_song["levels"].size() > 0:
                var first_level = first_song["levels"].keys()[0]
                var chart_path = "Assets/Tracks/%s/Chart_%s.json" % [song_id, first_level]
                var chart_json_str = phi_info_api.GetAssetText(chart_path)
                if chart_json_str:
                    test_output += " >> 成功读取谱面文本 (%s), 长度: %d 字符\n" % [first_level, chart_json_str.length()]
                    print(chart_json_str)
                else:
                    test_output += " >> 无法读取谱面文本 (%s)\n" % chart_path

            # 测试获取音频
            var music_path = "Assets/Tracks/%s/music.wav" % song_id
            var audio_stream = phi_info_api.GetAssetMusic(music_path)
            if audio_stream:
                test_output += " >> 成功解析音频 (%s), 长度: %.1f 秒\n" % [music_path, audio_stream.get_length()]
                # 如果场景中有 AudioStreamPlayer, 可直接赋予它并播放
                if has_node("AudioStreamPlayer"):
                    var player = get_node("AudioStreamPlayer")
                    player.stream = audio_stream
                    player.play()
            else:
                test_output += " >> 无法读取音频 (%s)\n" % music_path
                    
            # 导出其他信息的统计
            var chapters_json = phi_info_api.GetChaptersJson()
            var avatars_json = phi_info_api.GetAvatarsJson()
            var tips = phi_info_api.GetTips()
            var collection_json = phi_info_api.GetCollectionJson()
            var catalog_json = phi_info_api.GetAssetCatalogJson()
            
            test_output += "\n--- 更多数据统计 ---\n"
            test_output += "章节数量: %d\n" % _get_json_count(chapters_json)
            test_output += "头像数量: %d\n" % _get_json_count(avatars_json)
            test_output += "Tips 数量: %d\n" % tips.size()
            test_output += "合集数量: %d\n" % _get_json_count(collection_json)
            test_output += "资源目录项: %d\n" % _get_json_count(catalog_json)
            
            # 测试获取图片
            var ill_path = "Assets/Tracks/%s/Illustration.jpg" % song_id
            var image = phi_info_api.GetAssetImage(ill_path)
            if image:
                $TextureRect.texture = ImageTexture.create_from_image(image)
                test_output += " >> 成功解析图片 (%s), %dx%d\n" % [ill_path, image.get_width(), image.get_height()]
            else:
                test_output += " >> 无法读取图片 (%s)\n" % ill_path
    
    label.text = test_output

func _get_json_count(json_str: String) -> int:
    var j = JSON.new()
    if j.parse(json_str) == OK:
        if j.data is Array:
            return j.data.size()
        if j.data is Dictionary:
            return j.data.size()
    return 0

func _on_init_progress(status: String, progress: float) -> void:
    label.text = status
    progress_bar.value = progress
