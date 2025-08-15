# AutoSizer is just a class to remove the hassle of code duplication. Feel free to remove it, if you only need one type of Label.
class_name AutoSizer

static func update_font_size_label(label: AutoSizeLabel) -> void:
    _update_font_size(label, "font", "font_size", Vector2i(label.min_font_size, label.max_font_size), label.text)

static func _update_font_size(label: Control, font_name: StringName, font_style_name: StringName, font_size_range: Vector2i, text: String) -> void:
    var font := label.get_theme_font(font_name)

    var line := TextLine.new()
    line.direction = label.text_direction as TextServer.Direction
    line.flags = TextServer.JUSTIFICATION_NONE
    line.alignment = HORIZONTAL_ALIGNMENT_LEFT
    
    while true:
        line.clear()
        
        var mid_font_size := font_size_range.x + roundi((font_size_range.y - font_size_range.x) * 0.5)
        if !line.add_string(text, font, mid_font_size):
            push_warning("Could not create a string!")
            return
        
        var text_width := line.get_line_width()
        if text_width >= floori(label.size.x):
            if font_size_range.y == mid_font_size:
                break
            
            font_size_range.y = mid_font_size
        
        if text_width < floori(label.size.x):
            if font_size_range.x == mid_font_size:
                break
            
            font_size_range.x = mid_font_size
    
    label.add_theme_font_size_override(font_style_name, font_size_range.x)
