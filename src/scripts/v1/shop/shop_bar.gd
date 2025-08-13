extends Control

@export var indicate_margin: float = 43.0
@onready var indicate_tween: Tween
signal shop_bar_button_pressed

func indicate_move(n: float = 1) -> void:
    indicate_tween = get_tree().create_tween()
    var indicate_pos = ($ShopButton.position.x) + ($ShopButton/TextureButton.size.x) * ($ShopButton.scale.x) / 2 - ($Indicate.size.x) / 2 + (($Indicate.size.x) + indicate_margin) * (n - 1)
    # indicate_tween.tween_property($MarginContainer, "theme_override_constants/margin_left", indicate_start + (indicate_width + indicate_margin) * (n - 1), 0.5)
    indicate_tween.set_ease(Tween.EASE_IN_OUT)
    indicate_tween.set_trans(Tween.TRANS_CUBIC)
    indicate_tween.tween_property($Indicate, "position", Vector2(indicate_pos, $Indicate.position.y), 0.3)
    indicate_tween.play()
    
func on_shop_bar_button_pressed(n: int = 1) -> void:
    $Button.play()
    indicate_move(n)
#    切换回调
    shop_bar_button_pressed.emit(n)
