extends ScrollContainer

@export var dataOffCount: int = 5
@export var dataOffPrecent: float = 0.8
@export var batch_size: int = 5  # 每批加载数量
@export var load_delay: float = 0.01  # 批处理间隔秒数
@export var is_illustration: bool = false

var ShopSong = preload("res://components/v1/ShopSong.tscn")
var songIDs = {}
var illustrations = []  # 用于存储插图名
var songs_to_load = []  # 待加载的歌曲数据队列
var loading_in_progress = false
var placeholder_texture = preload("res://assets/v1/SingleChapterCoverBlur.png")

func _ready() -> void:
    # 预加载占位图
    placeholder_texture = preload("res://assets/v1/SingleChapterCoverBlur.png")
    # 异步加载TSV数据
    _load_song_data_async()
    $/root/Shop/bgMusic.play()

func _load_song_data_async() -> void:
    # 使用线程避免主线程阻塞
    var thread = Thread.new()
    thread.start(_thread_load_song_data.bind(thread))

func _thread_load_song_data(thread: Thread) -> void:
    # 后台加载歌曲数据
    var tsv_path = "res://assets/pigeon/info/info.tsv" if FileAccess.file_exists("res://assets/pigeon/info/info.tsv") else "res://assets/pigeon-default/info/info.tsv"
    songIDs = PhiSaveTools.parse_tsv_data([
        "songID", "songName", "songArtist", "illustrator",
        "EZ", "HD", "IN", "AT", "SP"
    ], tsv_path)
    var illustrationsT = FileAccess.open("res://assets/pigeon/info/illustration.txt", FileAccess.READ)
    if illustrationsT:
        illustrationsT = illustrationsT.get_as_text().split("\r\n")
        for song in songIDs:
            for illustration in illustrationsT:
                if songIDs[song]["songName"] == illustration:
                    illustrations.append(song)
                    break
    else:
        assert(illustrationsT)
        push_error("Get illustrations FAILED")
        illustrations = []


    # 数据准备好后回调到主线程
    call_deferred("_on_song_data_loaded", thread)

func _on_song_data_loaded(thread: Thread) -> void:
    thread.wait_to_finish()
    
    var paidSongs
    var paidIllustrations
    var songs = [{}, {}]

    if not is_illustration:
        paidSongs = SaveWorker.Songs.new().getPaidedSongs()
        for songID in songIDs:
            if songID in paidSongs[0]:
                songs[0][songID] = songIDs[songID]
            elif songID in paidSongs[1]:
                songs[1][songID] = songIDs[songID]
    else:
        paidIllustrations = SaveWorker.Illustrations.new().getPaidedIllustrations()
        for songID in illustrations:
            if songID in paidIllustrations[0]:
                songs[0][songID] = songIDs[songID]
            elif songID in paidIllustrations[1]:
                songs[1][songID] = songIDs[songID]
    
    var offSongs = []
    for i in range(dataOffCount):
        if songs[1].size() > 0:
            offSongs.append(songs[1].keys()[randi() % songs[1].size()])
    
    # 准备未购买歌曲数据
    for song in songs[1]:
        var songItem = songs[1][song]
        var data = int(randf_range(8, 17) * 100) / 100.0
        var dataOffPrecentRand = randi_range(dataOffPrecent * 10.0 - 5.0, dataOffPrecent * 10.0) / 10.0
        var illustration = "res://assets/pigeon/illustrationLowRes/%s.png" % song
        
        songs_to_load.push_back({
            "itemName": songItem["songName"],
            "data": "%.2f MB" % data,
            "dataOff": "%.2f MB" % (int(data * dataOffPrecentRand * 100) / 100.0) if song in offSongs else "",
            "dataOffPrecent": dataOffPrecentRand if song in offSongs else 0.0,
            "illustration": illustration,
            "isIllustration": is_illustration,
            "isSoldOut": false
        })
    
    # 准备已购买歌曲数据
    for song in songs[0]:
        var songItem = songs[0][song]
        var data = int(randf_range(4, 8) * 100) / 100.0
        var illustration = "res://assets/pigeon/illustrationLowRes/%s.png" % song
        
        songs_to_load.push_back({
            "itemName": songItem["songName"],
            "data": "%.2f MB" % data,
            "dataOff": "",
            "dataOffPrecent": 0.0,
            "illustration": illustration,
            "isIllustration": is_illustration,
            "isSoldOut": true
        })
    
    # 开始分批加载UI
    loading_in_progress = true
    call_deferred("_load_next_batch")

func _load_next_batch() -> void:
    if songs_to_load.is_empty():
        loading_in_progress = false
        print("All songs loaded")
        return
    
    # 处理当前批次
    var count = min(batch_size, songs_to_load.size())
    for i in range(count):
        var song_data = songs_to_load.pop_front()
        _create_song_item(song_data)
    
    # 继续下一批加载
    if not songs_to_load.is_empty():
        get_tree().create_timer(load_delay).timeout.connect(_load_next_batch, CONNECT_ONE_SHOT)

func _create_song_item(song_data: Dictionary) -> void:
    var songContainer = ShopSong.instantiate()
    songContainer.itemName = song_data["itemName"]
    songContainer.data = song_data["data"]
    songContainer.dataOff = song_data["dataOff"]
    songContainer.dataOffPrecent = song_data["dataOffPrecent"]
    songContainer.isIllustration = song_data["isIllustration"]
    songContainer.isSoldOut = song_data["isSoldOut"]
    
    # 设置图片（使用占位符异步加载）
    songContainer.set_illustration_async(song_data["illustration"], placeholder_texture)
    
    $MarginContainer/FlowContainer.add_child(songContainer)
