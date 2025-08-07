extends Control

@onready var ShopSongs = preload("res://components/v1/ShopSongs.tscn").instantiate()
@onready var ShopIllustrations = preload("res://components/v1/ShopSongs.tscn").instantiate()
@onready var ShopAvatar = preload("res://components/v1/ShopSongs.tscn").instantiate()
@onready var DataMining = preload("res://components/v1/DataMining.tscn").instantiate()

func queue_free_all() -> void:
    if $Pages/ShopSongs:
        $Pages.remove_child($Pages/ShopSongs)
    if ShopSongs.is_inside_tree():
        $Pages.remove_child(ShopSongs)
    if ShopIllustrations.is_inside_tree():
        $Pages.remove_child(ShopIllustrations)
    if ShopAvatar.is_inside_tree():
        $Pages.remove_child(ShopAvatar)
    if DataMining.is_inside_tree():
        $Pages.remove_child(DataMining)

func _ready() -> void:
    var remainingData = PhiSaveTools.DataSizeConverter.convert_to_highest((PhiSaveTools.DataSizeConverter.convert_from_kb(SaveWorker.Data.new().getData())))
    print("Current Data: ", remainingData)
    $/root/Shop/RemainingData/Data.text = "%.2f %s Data" % [remainingData[0],remainingData[1]]

    ShopIllustrations.pageType = ShopIllustrations.page_type.Illustration
    ShopAvatar.pageType = ShopAvatar.page_type.Avatar


func _on_button_pressed() -> void:
    await $TransitionManager.transition_to("res://scenes/global/splash0.tscn")

func _on_bg_music_finished() -> void:
    $bgMusic.play()


func _on_shop_bar_button_pressed(n) -> void:
    queue_free_all()
    match n:
        1:
            $Pages.add_child(ShopSongs)
        2:
            $Pages.add_child(ShopIllustrations)
        3:
            $Pages.add_child(ShopAvatar)
        4:
            $Pages.add_child(DataMining)
        _:
            print(n) # Replace with function body.


func _on_remaining_data_pressed() -> void:
    var DataDialog = preload("res://components/v1/DataDialog.tscn").instantiate()
    add_child(DataDialog)


func _on_remaining_data_long_pressed() -> void:
    var ShopLogin = preload("res://components/v1/ShopLogin.tscn").instantiate()
    add_child(ShopLogin)
