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

    dialog.checkout_cancelled.connect(func():_on_checkout_cancelled("Song"))
    dialog.checkout_confirmed.connect(func():_on_checkout_confirmed("Song"))
    get_tree().root.add_child(dialog)

func _on_checkout_cancelled(type : String= "" ):
    push_warning("%s Checkout cancelled" % type)
    
func _on_checkout_confirmed(type : String= "" ):
    SaveWorker.Songs.new().setPaidedSong(itemName)
    push_warning("%s Checkout confirmed" % type)
    isSoldOut = true
    $ShopSong/SoldOut.visible = isSoldOut
