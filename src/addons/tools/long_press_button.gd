extends TextureButton
class_name LongPressButton

# 自定义信号
signal short_pressed
signal long_pressed
signal press_cancelled  # 按压被取消（离开按钮区域）

# 长按阈值（单位：秒），可编辑器调整
@export_range(0.1, 3.0, 0.1) var long_press_threshold: float = 0.5
# 是否在长按后自动重置状态
@export var auto_reset: bool = true

# 状态变量
var _is_pressing := false
var _is_long_press_triggered := false
var _press_timer: Timer
var _last_mouse_position: Vector2

func _ready():
    # 初始化计时器
    _press_timer = Timer.new()
    add_child(_press_timer)
    _press_timer.one_shot = true
    _press_timer.timeout.connect(_on_long_press_timeout)
    
    # 连接内置信号
    button_down.connect(_on_button_down)
    button_up.connect(_on_button_up)
    mouse_entered.connect(_on_mouse_entered)
    mouse_exited.connect(_on_mouse_exited)

func _on_button_down():
    _is_pressing = true
    _is_long_press_triggered = false
    _last_mouse_position = get_global_mouse_position()
    _press_timer.start(long_press_threshold)

func _on_button_up():
    if not _is_pressing:
        return
    
    # 检查是否仍在按钮区域内
    var is_inside = _is_mouse_inside_button()
    
    if _is_long_press_triggered:
        # 长按已触发，忽略短按
        if auto_reset:
            _reset_state()
    elif is_inside:
        # 在阈值内释放 → 短按
        emit_signal("short_pressed")
        _reset_state()
    else:
        # 释放时已在区域外 → 取消
        emit_signal("press_cancelled")
        _reset_state()

func _on_long_press_timeout():
    if not _is_pressing:
        return
    
    # 检查是否仍在按钮区域内
    if _is_mouse_inside_button():
        _is_long_press_triggered = true
        emit_signal("long_pressed")
        
        # 自动重置状态
        if auto_reset:
            _reset_state()
    else:
        # 长按超时但已在区域外 → 取消
        emit_signal("press_cancelled")
        _reset_state()

func _on_mouse_entered():
    # 鼠标返回按钮区域时恢复按压状态
    if _is_pressing and not _is_long_press_triggered and _press_timer.time_left > 0:
        _press_timer.start(_press_timer.time_left)

func _on_mouse_exited():
    # 鼠标离开时暂停计时器
    if _is_pressing and not _is_long_press_triggered and _press_timer.time_left > 0:
        _press_timer.stop()

func _is_mouse_inside_button() -> bool:
    # 精确检测鼠标是否在按钮区域内
    var mouse_pos = get_global_mouse_position()
    var rect = get_global_rect()
    return rect.has_point(mouse_pos)

func _reset_state():
    _is_pressing = false
    _is_long_press_triggered = false
    _press_timer.stop()

# 手动重置状态（外部可调用）
func reset():
    _reset_state()

# 处理触摸屏输入
func _input(event):
    if event is InputEventScreenTouch:
        if event.pressed:
            # 模拟 button_down
            _on_button_down()
        else:
            # 模拟 button_up
            _on_button_up()
