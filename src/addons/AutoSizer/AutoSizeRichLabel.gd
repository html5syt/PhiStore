@tool
class_name AutoSizeRichLabel extends RichTextLabel

@export var min_font_size := 8 :
    set(v):
        min_font_size = clampi(v, 1, max_font_size)
        update()

@export var max_font_size := 56 :
    set(v):
        max_font_size = clampi(v, min_font_size, 191)
        update()

func _ready() -> void:
    item_rect_changed.connect(update)

func _set(property: StringName, value: Variant) -> bool:
    # Listen for changes to text
    if property == "text":
        text = value
        update()
        return true
    
    return false

func update() -> void:
    return AutoSizer.update_font_size_richlabel(self)
