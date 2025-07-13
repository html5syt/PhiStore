extends Control

func _on_button_pressed() -> void:
    await $TransitionManager.transition_to("res://scenes/splash.tscn")


func _on_back_pressed() -> void:
    push_warning("pressed")


func _on_bg_music_finished() -> void:
    $bgMusic.play()
