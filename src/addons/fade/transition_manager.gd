extends CanvasLayer

@export_file("*.tscn") var next_scene_path: String
@onready var _anim_player := $Background/AnimationPlayer

func _ready() -> void:
    _anim_player.play(&"Fade")

func transition_to(_next_scene: String = next_scene_path) -> void:
        # 禁用输入
    #get_tree().root.gui_disable_input=true
    # Play fade out
    _anim_player.play_backwards(&"Fade")
    await _anim_player.animation_finished
    
    # Change scene - using both options for compatibility
    if ResourceLoader.exists(_next_scene):
        var scene = load(_next_scene)
        if scene is PackedScene:
            get_tree().change_scene_to_packed(scene)
        else:
            get_tree().change_scene_to_file(_next_scene)
    
    # Play fade in
    _anim_player.play(&"Fade")
    await _anim_player.animation_finished
    ## 重新启用输入
    #get_tree().root.gui_disable_input=false
    #print("113313")
