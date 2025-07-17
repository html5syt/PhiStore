extends MarginContainer

@export var itemName: String
@export var data: String
@export var illustration: CompressedTexture2D
@export var dataOff : String = ""
@export var dataOffPrecent: float = 0
@export var isIllustration: bool = false
@export var isSoldOut: bool = false

var dialog: Control


func _ready() -> void:
    $ShopSong/itemNameLabel.text = itemName
    $ShopSong/Data/Data.text = data if dataOffPrecent == 0 else dataOff
    $ShopSong/DataOff.visible = dataOffPrecent != 0
    $ShopSong/DataOff/DataOff.text = "%s  OFF" % (str(int(dataOffPrecent*100))+"%") if dataOffPrecent!= 0 else ""
    $ShopSong/Illustration.texture = illustration
    $ShopSong/Type.texture = preload("res://assets/v1/ShopSong.tres") if not isIllustration else preload("res://assets/v1/ShopIllustration.tres")
    $ShopSong/SoldOut.visible = isSoldOut


func _on_button_pressed() -> void:
    if isSoldOut:
        return
    dialog = preload("res://components/v1/dialog_checkout.tscn").instantiate()
    var date = Time.get_datetime_dict_from_system()
    var datetime = Time.get_datetime_dict_from_system()
    dialog.date = "%d-%d-%d %d:%02d" % [
    datetime["year"],
    datetime["month"],
    datetime["day"],
    datetime["hour"],
    datetime["minute"]
]
    dialog.itemType = "Song" if not isIllustration else "Illustration"
    dialog.itemName = itemName
    dialog.amountData = data
    dialog.cheapData = dataOff if dataOffPrecent != 0  and dataOff != "" else ""

    dialog.checkout_cancelled.connect(_on_checkout_cancelled)
    dialog.checkout_confirmed.connect(_on_checkout_confirmed)
    get_tree().root.add_child(dialog)


var _texture_path: String = ""
var _loading_thread: Thread = null

func set_illustration_async(path: String, placeholder: Texture2D) -> void:
    # 先设置占位图
    if $ShopSong/Illustration:
        $ShopSong/Illustration.texture = placeholder
    
    # 保存路径以便后续加载
    _texture_path = path
    
    # 如果文件不存在，使用默认图片
    if not FileAccess.file_exists(path):
        print("Image not found: ", path)
        if $ShopSong/Illustration:
            $ShopSong/Illustration.texture = placeholder
        return
    
    # 启动线程加载图片
    if _loading_thread and _loading_thread.is_alive():
        _loading_thread.wait_to_finish()
    
    _loading_thread = Thread.new()
    _loading_thread.start(_load_texture_thread)

func _load_texture_thread() -> void:
    var texture: Texture2D = null
    
    # 使用 ResourceLoader 加载纹理资源
    if ResourceLoader.exists(_texture_path):
        var resource = ResourceLoader.load(_texture_path, "Texture2D", ResourceLoader.CACHE_MODE_IGNORE)
        if resource is Texture2D:
            texture = resource
        else:
            print("Loaded resource is not a Texture2D: ", _texture_path)
    else:
        print("Resource not found: ", _texture_path)
    
    # 完成后通知主线程
    call_deferred("_on_texture_loaded", texture)

func _on_texture_loaded(texture: Texture2D) -> void:
    if _loading_thread:
        _loading_thread.wait_to_finish()
        _loading_thread = null
    
    if texture and $ShopSong/Illustration:
        $ShopSong/Illustration.texture = texture
    elif $ShopSong/Illustration:
        $ShopSong/Illustration.texture = placeholder_texture

func _exit_tree() -> void:
    # 确保在节点销毁时清理线程
    if _loading_thread and _loading_thread.is_alive():
        _loading_thread.wait_to_finish()

# 添加一个默认的placeholder_texture
var placeholder_texture = preload("res://assets/v1/SingleChapterCoverBlur.png")

func _on_checkout_cancelled(type : String ):
    push_warning("%s Checkout cancelled" % type)
    
func _on_checkout_confirmed(type : String ,Data : String):
    SaveWorker.Songs.new().setPaidedSong(itemName)
    SaveWorker.Data.new().setData(Data,true)
    push_warning("%s Checkout confirmed" % type)
    isSoldOut = true
    $ShopSong/SoldOut.visible = isSoldOut
    var remainingData = PhiSaveTools.DataSizeConverter.convert_to_highest((PhiSaveTools.DataSizeConverter.convert_from_kb(SaveWorker.Data.new().getData())))
    print("Current Data: ", remainingData)
    $/root/Shop/RemainingData/Data.text = "%.2f %s Data" % [remainingData[0],remainingData[1]]
