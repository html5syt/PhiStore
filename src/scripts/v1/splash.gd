extends Node

func _ready() -> void:
    $ChangeSceneTimer.start()
    


func _on_change_scene_timer_timeout() -> void:
    $TransitionManager.transition_to("res://scenes/v1/Splash1.tscn")
