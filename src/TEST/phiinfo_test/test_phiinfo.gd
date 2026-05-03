extends Control

@onready var label: Label = $VBoxContainer/Label
@onready var file_dialog: FileDialog = $FileDialog
@onready var line_edit: LineEdit = $VBoxContainer/LineEdit
@onready var progress_bar: ProgressBar = $VBoxContainer/ProgressBar

var phi_info_api: PhiInfoAPI
const CLDB_PATH: String = "res://addons/PhiInfo/classdata.tpk" # 需要事先准备好该文件

# 本地资源类型枚举：将被作为 int 传入 C# 的 `GetResource`/`GetResourceAsync`
enum ResourceType {
    Illustration = 0,
    IllustrationBlur = 1,
    IllustrationLowRes = 2,
    Music = 3,
    Chart = 4,
    CollectionAsset = 5,
    Avatar = 6,
}

# 并发与请求控制变量
@export_group("Request Control")
@export var request_delay_ms: float = 200.0  # 每次请求后的强制等待时间
@export var max_parallel_requests: int = 1   # 同一时间内允许的最大并发数（设置为 1 即为严格串行）

var _active_request_count: int = 0           # 当前正在运行的请求数
var _request_semaphore: Semaphore = Semaphore.new() # 用于控制并发的信号量
var _request_mutex: Mutex = Mutex.new()      # 用于同步计数器的互斥锁

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
        
    if phi_info_api:
        phi_info_api.FreeContext() # 手动销毁已有上下文以加速 GC 并关闭流
    else:
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
    
    # 将原本密集的同步获取请求，改造成通过 _call_controlled_async 同步串行或低并发获取
    var version_info = await _call_controlled_async(phi_info_api.GetPhiVersionAsync)
    if not version_info:
        label.text = "错误: 无法获取游戏版本信息"
        return
    test_output += "游戏版本: %s (Code: %s)\n" % [version_info["name"], version_info["code"]]
    
    var songs = await _call_controlled_async(phi_info_api.GetSongsAsync)
    if not songs:
        label.text = "无法获取歌曲列表"
        return
    test_output += "共扫描到歌曲数量: %d\n" % songs.size()
    if songs.size() > 0:
        var first_song = songs[randi() % songs.size()]
        var song_id = first_song.get("id", "")
        test_output += "随机歌曲 ID: %s \n" % song_id
        
        # 测试获取谱面 JSON
        if first_song.has("levels") and first_song["levels"].size() > 0:
            var first_level_idx = first_song["levels"].keys()[0] # 这里的 key 通常是 "0", "1" 等字符串
            var chart_json_str = await async_get_resource(song_id, ResourceType.Chart, first_level_idx.to_int())
            if chart_json_str:
                test_output += " >> 成功读取谱面文本 (Index:%s), 长度: %d 字符\n" % [first_level_idx, chart_json_str.length()]
                print(chart_json_str.substr(0,200))
            else:
                test_output += " >> 无法读取谱面文本 (Index:%s)\n" % first_level_idx

        # 测试获取音频
        var audio_stream = await async_get_resource(song_id, ResourceType.Music)
        if audio_stream:
            test_output += " >> 成功解析音频, 长度: %.1f 秒\n" % audio_stream.get_length()
            if has_node("AudioStreamPlayer"):
                var player = get_node("AudioStreamPlayer")
                player.stream = audio_stream
                player.play()
        else:
            test_output += " >> 无法读取音频\n"
                
        # 异步获取更多信息
        var chapters = await _call_controlled_async(phi_info_api.GetChaptersAsync)
        var avatars = await _call_controlled_async(phi_info_api.GetAvatarsAsync)
        var tips = await _call_controlled_async(phi_info_api.GetTipsAsync)
        var collection = await _call_controlled_async(phi_info_api.GetCollectionAsync)
        var catalog = await _call_controlled_async(phi_info_api.GetAssetCatalogAsync)
        
        test_output += "\n--- 更多数据统计 ---\n"
        test_output += "章节数量: %d\n" % chapters.size()
        test_output += "头像数量: %d\n" % avatars.size()
        test_output += "Tips 数量: %d\n" % tips.size()
        test_output += "合集数量: %d\n" % collection.size()
        test_output += "资源目录项: %d\n" % catalog.size()
        
        # 测试Tips和收藏品
        if tips.size() > 0:
            var rand_tip = tips[randi() % tips.size()]
            print("随机 Tip: ", rand_tip)
        
        if collection.size() > 0:
            var rand_col = collection[randi() % collection.size()]
            print("随机 收藏品: ", str(rand_col).substr(0, 200))
        
        # 测试获取图片
        var image = await async_get_resource(song_id, ResourceType.Illustration)
        if image:
            $TextureRect.texture = ImageTexture.create_from_image(image)
            test_output += " >> 成功解析图片, %dx%d\n" % [image.get_width(), image.get_height()]
        else:
            test_output += " >> 无法读取曲绘图片\n"
        test_output += "用时 %f ms\n" % float((Time.get_ticks_usec()-start)/1000)

    label.text = test_output

func _on_init_progress(state: int, progress: float) -> void:
    var status_text = ""
    match state:
        0: status_text = "初始化开始..." # Starting
        1: status_text = "正在读取资源文件..." # ReadingFiles
        2: status_text = "正在挂载资源提供者..." # MountingProvider
        3: status_text = "正在构建资源上下文 (较耗时)..." # BuildingContext
        4: status_text = "初始化完成！" # Completed
        5: status_text = "发生错误！" # Error
        _: status_text = "未知状态: %d" % state
        
    #label.text = test_output
    progress_bar.value = progress

## 简洁的资源异步请求封装：隐藏 Callable 包装，直接返回资源结果
func async_get_resource(sid: String, rtype: int, diff: int = -1) -> Variant:
    return await _call_controlled_async(func(): return phi_info_api.GetResourceAsync(sid, rtype, diff))

## 通用异步请求控制器，提供并发限制和延时等待功能
## 防止请求速率过快（特别是在并发较高或网络解包模式下）
func _call_controlled_async(request_callable: Callable) -> Variant:
    # 1. 这里根据 max_parallel_requests 来限制并发
    # 如果当前已达到最大并发，且信号量机制不支持异步等待，我们简单的在这里循环等待
    while true:
        _request_mutex.lock()
        if _active_request_count < max_parallel_requests:
            _active_request_count += 1
            _request_mutex.unlock()
            break
        _request_mutex.unlock()
        await get_tree().create_timer(0.05).timeout # 等待 50ms 后再次尝试
        
    # 2. 调用原始异步函数
    var request = request_callable.call()
    var result = await request.Completed
    
    # 3. 延时控制：执行任务后的强制冷却期
    if request_delay_ms > 0:
        await get_tree().create_timer(request_delay_ms / 1000.0).timeout
    
    # 4. 释放计数器
    _request_mutex.lock()
    _active_request_count -= 1
    _request_mutex.unlock()
    
    return result

func _on_audio_stream_player_finished() -> void:
    $AudioStreamPlayer.play()
