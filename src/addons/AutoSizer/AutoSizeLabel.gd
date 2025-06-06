@tool
class_name AutoSizeLabel extends Label

@export var min_font_size := 60 :
    set(v):
        min_font_size = clampi(v, 1, max_font_size)
        update()

@export var max_font_size := 75 :
    set(v):
        max_font_size = clampi(v, min_font_size, 191)
        update()

func _ready() -> void:
    clip_text = true
    item_rect_changed.connect(update)

func _set(property: StringName, value: Variant) -> bool:
    # Listen for changes to text
    if property == "text":
        text = value
        update()
        return true
    
    return false

func update() -> void:
    return AutoSizer.update_font_size_label(self)
