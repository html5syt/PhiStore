extends Control

@export_category("结算对话框信息")
@export var date: String
@export var itemType: String
@export var itemName: String
@export var serviceFee: String = "512.000  KB"
@export var cheapData: String
@export var amountData: String

signal checkout_confirmed
signal checkout_cancelled

func _ready() -> void:
    $Dialog/Description/Date.text = date
    $Dialog/Description/Name.text = "%s (New %s)" % [itemName, itemType]
    $Dialog/Description/ServiceFee.text = serviceFee
    $Dialog/Description/cheapData.visible = true if cheapData else false
    $Dialog/Description/amountData.visible = true if not cheapData else false
    $Dialog/Description/cheapData.text = "[b][s]%s[/s][/b]
%s" % [amountData, cheapData] if cheapData else ""
    $Dialog/Description/amountData.text = amountData if not cheapData else ""
    $AnimationPlayer.play(&"in")


func _on_bg_cancelled() -> void:
    $AnimationPlayer.play_backwards(&"in")
    await get_tree().create_timer(0.6).timeout
    checkout_cancelled.emit()
    if self.is_inside_tree():
        self.queue_free()

func _on_confirmed() -> void:
    $AnimationPlayer.play_backwards(&"in")
    await get_tree().create_timer(0.6).timeout
    checkout_confirmed.emit()
    if self.is_inside_tree():
        self.queue_free()
