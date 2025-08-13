@tool
@icon("res://addons/autoscroll_container/autoscroll_container.svg")
class_name AutoScrollContainer
extends ScrollContainer

## AutoscrollContainer
##
## A [ScrollContainer] that automatically scrolls a specified amount after a specified amount of
## time passes with no caught input (to this specific controll).

## Amount of time to wait in seconds before scrolling.
@export var scroll_time_sec:float = 4

## The amount of pixels per second to scroll.
## Since the amount of pixels scrolled is a intiger value, a non zero value here will grantee that
## the amount of pixels scrolled will be at least one
## (preserving the approiprate direction/sign, though).
@export var scroll_px_per_sec := Vector2.ONE * 3

## Autoscroll in editor.
@export var in_editor := false

## If true, when scrolling to the end, it will reverse the scroll direction.
@export var reverse_on_end := false
@export var resverse_idle_time:float = 2.0

var reserve = scroll_state.LEFT

enum scroll_state{
    LEFT,
    RIGHT
}

## Allow the timer counting down to start scrolling to count even when the tree is paused.
## This will NOT effect weather or not the actual scrolling will process when paused,
## [member Node.process_mode] must be used to control this behaviour.
@export var scroll_time_process_always := false

## Allow the timer counting down to ignore time scale.
@export var scroll_time_ignore_time_scale := false

var _current_timer:SceneTreeTimer = null
var current_scroll_sign := Vector2.ONE

func _ready():
    restart_scroll_timer()
    current_scroll_sign = Vector2(sign(scroll_px_per_sec.x), sign(scroll_px_per_sec.y))

func _gui_input(_event:InputEvent):
    restart_scroll_timer()

func _process(delta: float):
    if not in_editor and Engine.is_editor_hint():
        return
    
    var h_scroll: HScrollBar = get_h_scroll_bar()
    var v_scroll: VScrollBar = get_v_scroll_bar()
    
    # 检查内容是否超出容器
    var h_scroll_needed: bool = (h_scroll.max_value - h_scroll.page) > 0
    var v_scroll_needed: bool = (v_scroll.max_value - v_scroll.page) > 0
    # 如果两个方向都不需要滚动，则直接返回
    if not h_scroll_needed and not v_scroll_needed:
        return
    
    if _current_timer != null and _current_timer.time_left <= 0:
        # 处理反向滚动逻辑
        if reverse_on_end:
            # 水平方向
            if h_scroll_needed:
                if current_scroll_sign.x > 0: # 向右滚动
                    if scroll_horizontal >= h_scroll.max_value - h_scroll.page:
                        if reserve == scroll_state.LEFT:
                            await get_tree().create_timer(resverse_idle_time).timeout
                            reserve = scroll_state.RIGHT
                            return
                        current_scroll_sign.x = -current_scroll_sign.x
                elif current_scroll_sign.x < 0: # 向左滚动
                    if scroll_horizontal <= 0:
                        if reserve == scroll_state.RIGHT:
                            await get_tree().create_timer(resverse_idle_time).timeout
                            reserve = scroll_state.LEFT
                            return
                        current_scroll_sign.x = -current_scroll_sign.x
            
            # 垂直方向
            if v_scroll_needed:
                if current_scroll_sign.y > 0: # 向下滚动
                    if scroll_vertical >= v_scroll.max_value - v_scroll.page:
                        current_scroll_sign.y = -current_scroll_sign.y
                elif current_scroll_sign.y < 0: # 向上滚动
                    if scroll_vertical <= 0:
                        current_scroll_sign.y = -current_scroll_sign.y
        
        # 计算实际滚动速度（考虑方向）
        var current_vel = Vector2(
            scroll_px_per_sec.x * current_scroll_sign.x,
            scroll_px_per_sec.y * current_scroll_sign.y
        )
        
        # 计算滚动量
        var scroll_diff_x = delta * current_vel.x
        var scroll_diff_y = delta * current_vel.y
        
        # 确保至少滚动1像素（保留方向）
        var scroll_diff := Vector2i(
            round(scroll_diff_x) if abs(scroll_diff_x) >= 1.0 else sign(scroll_diff_x),
            round(scroll_diff_y) if abs(scroll_diff_y) >= 1.0 else sign(scroll_diff_y)
        )
        
        # 应用滚动
        scroll_horizontal += scroll_diff.x
        scroll_vertical += scroll_diff.y

## Restarts the scroll timer. Will stop the scrolling of this container, but not reset it.
func restart_scroll_timer():
    if not in_editor and Engine.is_editor_hint():
        return
    var tree := get_tree()
    if tree != null:
        var timer := tree.create_timer(scroll_time_sec,
                                       scroll_time_process_always,
                                       false,
                                       scroll_time_ignore_time_scale
                                      )
        _current_timer = timer
