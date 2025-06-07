extends CanvasLayer

# 正确加载和调用 C# 类的方式
var pack_save = PackSave.new()  # 创建 C# 类的实例

func _ready():
    # 添加测试按钮
    var get_button = Button.new()
    get_button.text = "测试获取存档"
    get_button.position = Vector2(50, 50)
    get_button.size = Vector2(200, 40)
    get_button.connect("pressed", _test_get)
    add_child(get_button)
    
    var upload_button = Button.new()
    upload_button.text = "测试上传存档"
    upload_button.position = Vector2(50, 100)
    upload_button.size = Vector2(200, 40)
    upload_button.connect("pressed", _test_upload)
    add_child(upload_button)

func _test_get():
    print("开始获取存档...")
    pack_save.GetSave("user://.save", "user://PhigrosSaves-1.json")
    
func _test_upload():
    print("开始上传存档...")
    pack_save.Upload("user://PhigrosSaves-1.json", "user://.save-1")
