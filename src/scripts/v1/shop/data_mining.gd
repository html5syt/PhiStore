extends Control

enum itemType {
    Data,
    Illustration,
    Avatar,
    Collection,
    Null
}

enum itemColor {
    White,
    Blue,
    Purple,
    Yellow
}

var stateCount :int =0
# 0：未开始/已结束
# -1：single结束
# 1~10：ten正在进行

var lock = false
 
@onready var Name :String = "":
    set(value):
        Name = value
        $Question/Name.text = value
@onready var Type :itemType:
    set(value):
        Type = value
        match value:
            itemType.Data:
                $Question/Icon/TextureRect.texture = preload("res://assets/v1/ShopData.tres")
            itemType.Illustration:
                $Question/Icon/TextureRect.texture = preload("res://assets/v1/ShopIllustration.tres")
            itemType.Avatar:
                $Question/Icon/TextureRect.texture = preload("res://assets/v1/ShopAvatar.tres")
            itemType.Collection:
                $Question/Icon/TextureRect.texture = preload("res://assets/v1/file.png")
            itemType.Null:
                $Question/Icon/TextureRect.texture = preload("res://assets/v1/shape037_style01_color08.png")

@onready var textColor :itemColor:
    set(value):
        textColor = value
        match value:
            itemColor.White:
                $Question/Name.set("theme_override_colors/font_color",Color.WHITE)
            itemColor.Blue:
                $Question/Name.set("theme_override_colors/font_color",Color.BLUE)
            itemColor.Purple:
                $Question/Name.set("theme_override_colors/font_color",Color.PURPLE)
            itemColor.Yellow:
                $Question/Name.set("theme_override_colors/font_color",Color.YELLOW)

func setOut():
    $Question/NameOut.text = Name
    match Type:
        itemType.Data:
            $Question/IconOut/TextureRect.texture = preload("res://assets/v1/ShopData.tres")
        itemType.Illustration:
            $Question/IconOut/TextureRect.texture = preload("res://assets/v1/ShopIllustration.tres")
        itemType.Avatar:
            $Question/IconOut/TextureRect.texture = preload("res://assets/v1/ShopAvatar.tres")
        itemType.Collection:
            $Question/IconOut/TextureRect.texture = preload("res://assets/v1/file.png")
        itemType.Null:
            $Question/IconOut/TextureRect.texture = preload("res://assets/v1/shape037_style01_color08.png")
    match textColor:
        itemColor.White:
            $Question/NameOut.set("theme_override_colors/font_color",Color.WHITE)
        itemColor.Blue:
            $Question/NameOut.set("theme_override_colors/font_color",Color.BLUE)
        itemColor.Purple:
            $Question/NameOut.set("theme_override_colors/font_color",Color.PURPLE)
        itemColor.Yellow:
            $Question/NameOut.set("theme_override_colors/font_color",Color.YELLOW)

func getAllItems() -> Dictionary:
    var items = {}
    var tmp2 = SaveWorker.Illustrations.new()
    for item in tmp2.getPaidedIllustrations()[1]:
        item = tmp2.ID_to_Name(item)
        items[item] = {"type": itemType.Illustration,"color":itemColor.White}
    for item in SaveWorker.Avatars.new().getPaidedAvatars()[1]:
        if item.length() == 0:
            continue
        items[item] = {"type": itemType.Avatar}
        if item.length() > 10:
            items[item]["color"] = itemColor.Purple
        else:
            items[item]["color"] = itemColor.Blue
    var tmp = SaveWorker.Collections.new().getGotCollections()[1]
    for item in tmp:
        var tmpName = tmp[item]["name"]
        items[tmpName] = {"type": itemType.Collection}
        var gold = ["周边·判定线抱枕"]
        if tmpName in gold:
            items[tmpName]["color"] = itemColor.Yellow
        else:
            items[tmpName]["color"] = itemColor.White
            # 收集品颜色由抽奖部分决定
    # 硬编码Data
    for item in ["256KB"]:
        items[item] = {"type": itemType.Data,"color":itemColor.White}
    for item in ["512KB"]:
        items[item] = {"type": itemType.Data,"color":itemColor.Blue}
    for item in ["2MB","16MB"]:
        items[item] = {"type": itemType.Data,"color":itemColor.Purple}
    for item in ["32MB","64MB","128MB"]:
        items[item] = {"type": itemType.Data,"color":itemColor.Yellow}

    items["NULL"] = {"type": itemType.Null,"color":itemColor.White}

    return items

func randomChoice():
    var items = getAllItems()
    var choice = {
        itemType.Illustration: 0.03,
        itemType.Avatar: 0.05,
        itemType.Collection: 0.12,
        itemType.Data: 0.20,
        itemType.Null: 0.60
    }
    var result = {"type":PhiSaveTools.weighted_random(choice)}
    match result["type"]:
        itemType.Illustration:
            result["color"] = itemColor.White
        itemType.Avatar:
            result["color"] = PhiSaveTools.weighted_random({itemColor.Blue: 0.7, itemColor.Purple: 0.3})
        itemType.Collection:
            result["color"] = PhiSaveTools.weighted_random({itemColor.White: 0.7,itemColor.Blue: 0.19,itemColor.Purple: 0.1, itemColor.Yellow: 0.01})
        itemType.Data:
            result["color"] = PhiSaveTools.weighted_random({itemColor.White: 0.7,itemColor.Blue: 0.15,itemColor.Purple: 0.14, itemColor.Yellow: 0.01})
        itemType.Null:
            result["color"] = itemColor.White
    choice = {}
    for item in items:
        if items[item]["type"] == result["type"] and items[item]["color"] == result["color"]:
            choice[item] = {"type": items[item]["type"], "color": items[item]["color"]}
    result = {}
    if choice.size() != 0:
        var index = choice.keys().pick_random()
        result[index] = choice[index]
    if result == {}:
        randomChoice()
    match result[result.keys()[0]]["type"]:
        itemType.Illustration:
            SaveWorker.Illustrations.new().setPaidedIllustration(result.keys()[0])
        itemType.Avatar:
            SaveWorker.Avatars.new().setPaidedAvatar(result.keys()[0])
        itemType.Collection:
            SaveWorker.Collections.new().gettingCollection(result.keys()[0],0,-1)
        itemType.Data:
            SaveWorker.Data.new().setData(result.keys()[0],false)
            var remainingData = PhiSaveTools.DataSizeConverter.convert_to_highest((PhiSaveTools.DataSizeConverter.convert_from_kb(SaveWorker.Data.new().getData())))
            print("Current Data: ", remainingData)
            $/root/Shop/RemainingData/Data.text = "%.2f %s Data" % [remainingData[0],remainingData[1]]
        itemType.Null:
            pass
    var remainingData2 = PhiSaveTools.DataSizeConverter.convert_to_highest((PhiSaveTools.DataSizeConverter.convert_from_kb(SaveWorker.Data.new().getData())))
    print("Current Data: ", remainingData2)
    $/root/Shop/RemainingData/Data.text = "%.2f %s Data" % [remainingData2[0],remainingData2[1]]
    return result

func single():
    if not $Question/AnimationPlayer.is_playing() and not lock:
        if stateCount == 0:
            if SaveWorker.Data.new().getData() < PhiSaveTools.DataSizeConverter.convert_to_kb(PhiSaveTools.DataSizeConverter.convert_from_highest("1MB")):
                push_warning("No enough money")
                $NoEnoughMoney.visible = true
                return
            else:
                SaveWorker.Data.new().setData(1024,true)
                $NoEnoughMoney.visible = false
            lock = true
            var item = randomChoice()
            print(item)
            print()
            while item == {}:
                item = randomChoice()
            Name = item.keys()[0] 
            textColor = item.values()[0]["color"]
            Type = item.values()[0]["type"]
            $Question/AnimationPlayer.play(&"in")
            setOut()
            stateCount = -1
            lock = false
        else:
            lock = true
            $Question/AnimationPlayer.play(&"out")
            lock = false
            # await get_tree().create_timer(0.5).timeout
            # $Question/AnimationPlayer.play(&"RESET")
            stateCount = 0

func ten():
    print(stateCount)
    if not $Question/AnimationPlayer.is_playing() and not lock:
        print(stateCount)
        if stateCount == 0:
            if SaveWorker.Data.new().getData() < PhiSaveTools.DataSizeConverter.convert_to_kb(PhiSaveTools.DataSizeConverter.convert_from_highest("8MB")):
                push_warning("No enough money")
                $NoEnoughMoney.visible = true
                return
            else:
                SaveWorker.Data.new().setData(819.2,true)
                $NoEnoughMoney.visible = false
            lock = true
            var item = randomChoice()
            print(item)
            print()
            while item == {}:
                item = randomChoice()
            Name = item.keys()[0]
            textColor = item.values()[0]["color"]
            Type = item.values()[0]["type"]
            $Question/AnimationPlayer.play(&"in")
            $MBoverlay.show()
            setOut()
            stateCount = 1
            lock = false
        elif stateCount > 0 and stateCount < 10:
            SaveWorker.Data.new().setData(819.2,true)
            lock = true
            var item = randomChoice()
            print(item)
            print()
            while item == {}:
                item = randomChoice()
            Name = item.keys()[0]
            textColor = item.values()[0]["color"]
            Type = item.values()[0]["type"]
            $Question/AnimationPlayer.play(&"during")
            await get_tree().create_timer(0.6).timeout
            setOut()
            stateCount += 1
            lock = false
        elif stateCount >= 10:
            lock = true
            $MBoverlay.hide()
            $Question/AnimationPlayer.play(&"out")
            # await get_tree().create_timer(0.5).timeout
            # $Question/AnimationPlayer.play(&"RESET")
            stateCount = 0
            lock = false
