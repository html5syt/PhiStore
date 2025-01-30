def default_json():
    # 该函数返回一个字典，包含了默认的JSON数据（Flet无法读取到本地JSON文件）
    # 请按照JSON及字典格式填写并格式化。
    var = {
        "File": {
            "White": ["File,White", "File,White,White"],
            "Blue": ["File,Blue", "File,Blue,Blue"],
            "Purple": ["File,Purple", "File,Purple,Purple"],
            "Yellow": ["File,Yellow", "File,Yellow,Yellow"],
        },
        "Data": {
            "White": [262144],
            "Blue": [524288],
            "Purple": [2097152, 4194304, 8388608, 16777216],
            "Yellow": [33554432, 67108864, 134217728],
        },
        "Null": {"White": ["Null"]},
        "Avatar": {
            "Blue": ["Avatar,Blue", "Avatar,Blue,Blue"],
            "Purple": ["Avatar,Purple", "Avatar,Purple,Purple"],
        },
        "Illustration": {"White": ["Illustration,White", "Illustration,White,White"]},
    }

    return var
