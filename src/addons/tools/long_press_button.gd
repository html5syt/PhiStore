extends TextureButton
class_name LongPressButton

# 自定义信号：长按触发
signal long_pressed
# 长按阈值（单位：秒），可编辑器调整
@export var long_press_threshold: float = 0.5

var _is_pressing := false
var _press_timer: Timer

func _ready():
    # 初始化计时器
    _press_timer = Timer.new()
    add_child(_press_timer)
    _press_timer.one_shot = true
    _press_timer.timeout.connect(_on_long_press_timeout)
    
    # 连接按钮内置信号
    button_down.connect(_on_button_down)
    button_up.connect(_on_button_up)

func _on_button_down():
    _is_pressing = true
    _press_timer.start(long_press_threshold)  # 开始计时

func _on_button_up():
    _is_pressing = false
    if _press_timer.time_left > 0:
        # 在阈值内释放 → 短按
        _press_timer.stop()
    # 注意：短按逻辑由内置的 `pressed` 信号处理

func _on_long_press_timeout():
    if _is_pressing:
        emit_signal("long_pressed")  # 触发长按
