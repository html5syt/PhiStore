extends Control

@export_category("结算对话框信息")
@export var date: String
# @export var itemType: String
@export var itemName: String
@export var serviceFee: String = "512.000  KB"
@export var cheapData: String
@export var amountData: String
@export var itemType: page_type = page_type.Song  # 页面类型
enum page_type {
    Song,
    Illustration,
    Avatar
}

signal checkout_confirmed
signal checkout_cancelled

func _ready() -> void:
    $Dialog/Description/Date.text = date
    match itemType:
        page_type.Song:
            $Dialog/Description/Name.text = "%s (New %s)" % [itemName, "Song"]
        page_type.Illustration:
            $Dialog/Description/Name.text = "%s (New %s)" % [itemName, "Illustration"]
        page_type.Avatar:
            $Dialog/Description/Name.text = "%s (New %s)" % [itemName, "Avatar"]
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
    match itemType:
        page_type.Song:
            checkout_cancelled.emit(page_type.Song)
        page_type.Illustration:
            checkout_cancelled.emit(page_type.Illustration)
        page_type.Avatar:
            checkout_cancelled.emit(page_type.Avatar)
    if self.is_inside_tree():
        self.queue_free()

func _on_confirmed() -> void:
    if SaveWorker.Data.new().getData() < PhiSaveTools.DataSizeConverter.convert_to_kb(PhiSaveTools.DataSizeConverter.convert_from_highest(cheapData if cheapData else amountData)):
        push_warning("No enough money")
        $Dialog/NoEnoughMoney.visible = true
        return
    $AnimationPlayer.play_backwards(&"in")
    await get_tree().create_timer(0.6).timeout
    match itemType:
        page_type.Song:
            checkout_confirmed.emit(page_type.Song,cheapData if cheapData else amountData)
        page_type.Illustration:
            checkout_confirmed.emit(page_type.Illustration,cheapData if cheapData else amountData)
        page_type.Avatar:
            checkout_confirmed.emit(page_type.Avatar,cheapData if cheapData else amountData)
    if self.is_inside_tree():
        self.queue_free()
