extends Control

func _ready() -> void:
    $Label.text = "© Pigeon Games
Remake by Tim
Original: Phigros V1.6.11
Ver: %s" % ProjectSettings.get_setting("application/ExConfig/GameVersion")
    await $splashAnimation.animation_finished
    await get_tree().create_timer(1).timeout
    await $TransitionManager.transition_to("res://scenes/v1/splash/Splash.tscn")


func _on_developer_board_pressed() -> void:
    await $TransitionManager.transition_to("res://TEST/main.tscn")
