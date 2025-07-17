extends Control

func _ready() -> void:
    var remainingData = PhiSaveTools.DataSizeConverter.convert_to_highest((PhiSaveTools.DataSizeConverter.convert_from_kb(SaveWorker.Data.new().getData())))
    print("Current Data: ", remainingData)
    $/root/Shop/RemainingData/Data.text = "%.2f %s Data" % [remainingData[0],remainingData[1]]

func _on_button_pressed() -> void:
    await $TransitionManager.transition_to("res://scenes/splash.tscn")


func _on_back_pressed() -> void:
    push_warning("pressed")


func _on_bg_music_finished() -> void:
    $bgMusic.play()
