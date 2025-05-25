extends Control



func _on_button_pressed() -> void:
    await $TransitionManager.transition_to("res://scenes/splash.tscn")
