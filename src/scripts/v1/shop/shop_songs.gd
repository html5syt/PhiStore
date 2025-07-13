extends ScrollContainer

@export var dataOffCount: int = 5
@export var dataOffPrecent: float = 0.8

var ShopSong = preload("res://components/v1/ShopSong.tscn")
var songIDs = PhiSaveTools.parse_tsv_data([
    "songID", "songName", "songArtist", "illustrator",
    "EZ", "HD", "IN", "AT", "SP"
], "res://assets/pigeon/info/info.tsv" if FileAccess.file_exists("res://assets/pigeon/info/info.tsv") else "res://assets/pigeon-default/info/info.tsv")

func _ready() -> void:
    var paidSongs = SaveWorker.Songs.new().getPaidedSongs()
    var songs = [{}, {}]

    for songID in songIDs:
        if songID in paidSongs[0]:
            songs[0][songID] = songIDs[songID]
        elif songID in paidSongs[1]:
            songs[1][songID] = songIDs[songID]
    var offSongs = []
    for i in range(dataOffCount):
        offSongs.append(songs[1].keys()[randi() % songs[1].size()])
    for song in songs[1]:
        var songItem = songs[1][song]
        # 未购买歌曲
        var songContainer = ShopSong.instantiate()
        songContainer.itemName = songItem["songName"]
        var data = int(randf_range(8, 17) * 100) / 100.0
        var dataOffPrecentRand = randi_range(dataOffPrecent * 10.0 - 5.0, dataOffPrecent * 10.0) / 10.0
        var illustration = "res://assets/pigeon/illustrationLowRes/%s.png" % song
        songContainer.data = "%.2f MB" % data
        if song in offSongs:
            songContainer.dataOff = "%.2f MB" % (int(data*dataOffPrecentRand*100)/100.0)
            songContainer.dataOffPrecent = dataOffPrecentRand
            dataOffCount -= 1
        songContainer.illustration = load(illustration if FileAccess.file_exists(illustration) else "res://assets/v1/SingleChapterCoverBlur.png")
        $MarginContainer/FlowContainer.add_child(songContainer)
    for song in songs[0]:
        var songItem = songs[0][song]
        # 已购买歌曲
        var songContainer = ShopSong.instantiate()
        songContainer.itemName = songItem["songName"]
        var data = int(randf_range(4, 8) * 100) / 100.0
        var illustration = "res://assets/pigeon/illustrationLowRes/%s.png" % song
        songContainer.data = "%.2f MB" % data
        songContainer.illustration = load(illustration if FileAccess.file_exists(illustration) else "res://assets/v1/SingleChapterCoverBlur.png")
        songContainer.isSoldOut = true
        $MarginContainer/FlowContainer.add_child(songContainer)
