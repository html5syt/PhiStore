extends Control

func _ready() -> void:
    await $splashAnimation.animation_finished
    await get_tree().create_timer(3).timeout
    await $TransitionManager.transition_to("res://scenes/v1/splash/Splash.tscn")


func _on_developer_board_pressed() -> void:
    await $TransitionManager.transition_to("res://TEST/main.tscn")
