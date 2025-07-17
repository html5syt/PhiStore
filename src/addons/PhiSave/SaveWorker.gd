class_name SaveWorker


class Songs:
    # FreeSongs
    # PaidSongs
    var songIDs = PhiSaveTools.parse_tsv_data([
        "songID", "songName", "songArtist", "illustrator",
        "EZ", "HD", "IN", "AT", "SP"
    ], "res://assets/pigeon/info/info.tsv" if FileAccess.file_exists("res://assets/pigeon/info/info.tsv") else "res://assets/pigeon-default/info/info.tsv")
    func getAllPaidSongs() -> Array:
        var singleSongs = FileAccess.open("res://assets/pigeon/info/single.txt", FileAccess.READ)
        var paidSongs = []
        if singleSongs:
            singleSongs = singleSongs.get_as_text().split("\r\n")
            for song in songIDs:
                if songIDs[song]["songName"] in singleSongs:
                    paidSongs.append(song)
            return paidSongs
        else:
            assert(singleSongs)
            push_error("Get All Paid Songs FAILED")
            return []

    func getPaidedSongs() -> Array:
        var Phi_Save = FileAccess.open("user://PhigrosSaves.json", FileAccess.READ)
        if Phi_Save:
            Phi_Save = JSON.parse_string(Phi_Save.get_as_text())
        else:
            assert(Phi_Save)
            push_error("Save File Not Found")
            Phi_Save = {}

        var allPaidSongs = getAllPaidSongs()
        var paidSongs = []
        var unpaidsongs = []
        for song in allPaidSongs:
            if Phi_Save["gameKey"]["keyList"].has(songIDs[song]["songName"]):
                var game_key = Phi_Save["gameKey"]["keyList"][songIDs[song]["songName"]]
                if PhiSaveTools.parse_list_string(game_key["type"])[1] == "1":
                    paidSongs.append(song)
                else:
                    unpaidsongs.append(song)
            else:
                unpaidsongs.append(song)
        if paidSongs == []:
            push_warning("No Paid Songs Found")
        return [paidSongs, unpaidsongs]

    func setPaidedSong(songName: String):
        var Phi_Save = FileAccess.open("user://PhigrosSaves.json", FileAccess.READ)
        if Phi_Save:
            Phi_Save = JSON.parse_string(Phi_Save.get_as_text())
        else:
            assert(Phi_Save)
            push_error("Save File Not Found")
            Phi_Save = {}

        if not Phi_Save.has("gameKey"):
            Phi_Save["gameKey"] = {"keyList": {}}

        if not Phi_Save["gameKey"]["keyList"].has(songName):
            Phi_Save["gameKey"]["keyList"][songName] = {"flag": str([1]), "type": str([0, 1, 0, 0, 0])}
        else:
            var flag = PhiSaveTools.parse_list_string(Phi_Save["gameKey"]["keyList"][songName]["flag"])
            var type = PhiSaveTools.parse_list_string(Phi_Save["gameKey"]["keyList"][songName]["type"])
            var list = PhiSaveTools.concat_flag_and_type(flag, type)
            list[1] = 1
            flag = PhiSaveTools.split_flag_and_type(list)[0]
            type = PhiSaveTools.split_flag_and_type(list)[1]
            Phi_Save["gameKey"]["keyList"][songName] = {"flag": str(flag), "type": str(type)}

        var Phi_SaveD = Phi_Save.duplicate()
        Phi_Save = FileAccess.open("user://PhigrosSaves.json", FileAccess.WRITE)
        Phi_Save.store_string(JSON.stringify(Phi_SaveD, "\t" if OS.has_feature("debug") else ""))


class Data:
    var Phi_Save

    func _init() -> void:
        Phi_Save = FileAccess.open("user://PhigrosSaves.json", FileAccess.READ)
        if Phi_Save:
            Phi_Save = JSON.parse_string(Phi_Save.get_as_text())
        else:
            assert(Phi_Save)
            push_error("Save File Not Found")
            Phi_Save = {}

    func getData() -> int:
        var money = Phi_Save["gameProgress"]["money"]
        return PhiSaveTools.DataSizeConverter.new().convert_to_kb(money)

    func setData(dataDelta: Variant,down = false):
        var data = getData()
        if dataDelta is int:
            if down:
                data -= dataDelta
            else:
                data += dataDelta
            Phi_Save["gameProgress"]["money"] = PhiSaveTools.DataSizeConverter.new().convert_from_kb(data)
        else:
            dataDelta = dataDelta.strip_edges()
            dataDelta = PhiSaveTools.DataSizeConverter.new().convert_from_highest(dataDelta)
            if down:
                data -= PhiSaveTools.DataSizeConverter.new().convert_to_kb(dataDelta)
            else:
                data += PhiSaveTools.DataSizeConverter.new().convert_to_kb(dataDelta)
            Phi_Save["gameProgress"]["money"] = PhiSaveTools.DataSizeConverter.new().convert_from_kb(data)
        var Phi_SaveD = Phi_Save.duplicate()
        Phi_Save = FileAccess.open("user://PhigrosSaves.json", FileAccess.WRITE)
        Phi_Save.store_string(JSON.stringify(Phi_SaveD, "\t" if OS.has_feature("debug") else ""))
