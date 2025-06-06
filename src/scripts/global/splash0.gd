extends Control

func _choose_v1_ui() -> void:
    await $TransitionManager.transition_to("res://scenes/v1/Splash.tscn")


func _choose_v2_ui() -> void:
    await $TransitionManager.transition_to("res://TEST/main.tscn")
