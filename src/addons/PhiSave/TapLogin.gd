extends Node

class_name TapLogin

# 信号定义
signal device_flow_qr_ready(qr_url) # 设备码流程二维码就绪
signal device_flow_completed(token_data) # 设备码流程完成
signal auth_flow_completed(token_data) # 授权码流程完成
signal auth_flow_failed(error) # 授权码流程失败
signal auth_flow_need_code() # 授权码流程需要输入验证码
signal user_info_received(user_data) # 用户信息获取成功
signal cloud_user_created(cloud_data) # 云函数用户创建成功

# 常量定义
const DEVICE_CODE_URL = "https://accounts.tapapis.cn/oauth2/v1/device/code"
const TOKEN_URL = "https://accounts.tapapis.cn/oauth2/v1/token"
const AUTHORIZE_URL = "https://accounts.taptap.com/authorize"
const BASIC_INFO_URL = "https://open.tapapis.cn/account/basic-info/v1"

# 配置信息
var client_id = "rAK3FfdieFob2Nn8Am" # 替换为你的客户端ID
const CLIENT_KEY = "Qr9AEqtuoSVS3zeD6iVbM4ZC0AtkJcQ89tywVyi0" # 替换为您的client_key

# 流程状态变量
var device_code = ""
var polling_timer = null
var http_server = null
var server_port = 0
var code_verifier = ""
var state = ""
var redirect_uri = ""
var current_token_data = null # 存储获取到的token数据


# 入口
# 开始QR码流程：获取二维码和设备码
func start_QR_code_flow():
    var body = {
        "client_id": client_id,
        "response_type": "device_code",
        "scope": "public_profile",
        "version": "1.2.0",
        "platform": "godot",
        "info": JSON.stringify({"device_id": OS.get_model_name()})
    }
    
    var headers = ["Content-Type: application/x-www-form-urlencoded"]
    var http_request = HTTPRequest.new()
    add_child(http_request)
    http_request.request(DEVICE_CODE_URL, headers, HTTPClient.METHOD_POST, _encode_form(body))
    http_request.connect("request_completed", _on_device_code_response)

# 开始授权码流程
func start_browser_auth_flow():
    # 生成安全参数
    code_verifier = _generate_code_verifier(128)
    state = _generate_random_string(32, "abcdefghijklmnopqrstuvwxyz0123456789")
    
    # 启动本地HTTP服务器
    _start_local_server()
    # else:
    #     push_warning("Browser auth TCPServer not supported on web platform")
    #     # 打开浏览器进行授权
    #     auth_flow_need_code.emit()
    #     redirect_uri = "https://127.0.0.1:14514/authorize"
    #     _open_browser_for_auth()

# 手动用授权码（回调链接内code参数）交换令牌
func exchange_code_for_token(auth_code):
    var body = {
        "client_id": client_id,
        "grant_type": "authorization_code",
        "secret_type": "hmac-sha-1",
        "code": auth_code,
        "redirect_uri": redirect_uri,
        "code_verifier": code_verifier
    }
    
    var headers = ["Content-Type: application/x-www-form-urlencoded"]
    var http_request = HTTPRequest.new()
    add_child(http_request)
    var base_url = "" if not OS.has_feature("web") else "https://open-tapapis.bravely.pp.ua/?url="
    http_request.request_completed.connect(_on_token_response)
    http_request.request(base_url + TOKEN_URL, headers, HTTPClient.METHOD_POST, _encode_form(body))

# 处理设备码响应
func _on_device_code_response(result, response_code, headers, body):
    if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
        push_error("Failed to get device code")
        return
    
    var json = JSON.parse_string(body.get_string_from_utf8())
    if json == null:
        push_error("Invalid JSON response")
        return
    
    var response = json.data
    device_code = response.get("device_code", "")
    var verification_url = response.get("qrcode_url", "")
    # var interval = response.get("interval", 5)  # 设备码流程的轮询间隔
    var interval = 5 # 设备码流程的轮询间隔
    
    if verification_url and device_code:
        emit_signal("device_flow_qr_ready", verification_url)
        _start_polling(interval)

# 开始轮询设备登录状态
func _start_polling(interval):
    if polling_timer:
        polling_timer.stop()
        polling_timer.queue_free()
    
    polling_timer = Timer.new()
    add_child(polling_timer)
    polling_timer.wait_time = interval
    polling_timer.connect("timeout", _poll_device_status)
    polling_timer.start()

# 轮询设备登录状态
func _poll_device_status():
    var body = {
        "grant_type": "device_token",
        "client_id": client_id,
        "secret_type": "hmac-sha-1",
        "code": device_code,
        "version": "1.0",
        "platform": "godot",
        "info": JSON.stringify({"device_id": OS.get_model_name()})
    }
    
    var headers = ["Content-Type: application/x-www-form-urlencoded"]
    var http_request = HTTPRequest.new()
    add_child(http_request)
    http_request.request(TOKEN_URL, headers, HTTPClient.METHOD_POST, _encode_form(body))
    http_request.connect("request_completed", _on_poll_response)

# 处理轮询响应
func _on_poll_response(result, response_code, headers, body):
    if result != HTTPRequest.RESULT_SUCCESS:
        return
    
    if response_code == 200:
        var json = JSON.parse_string(body.get_string_from_utf8())
        if json != null:
            _cleanup_polling()
            current_token_data = json # 保存token数据
            emit_signal("device_flow_completed", json)
            
            # 自动进行后续请求
            _request_user_info(json)
    elif response_code != 400: # 400 表示授权仍在等待
        push_error("Polling failed: " + str(response_code))
        _cleanup_polling()

# 清理轮询资源
func _cleanup_polling():
    if polling_timer:
        polling_timer.stop()
        polling_timer.queue_free()
        polling_timer = null
    device_code = ""

# 生成128位code_verifier
func _generate_code_verifier(length):
    var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_"
    var verifier = ""
    for i in range(length):
        verifier += chars[randi() % chars.length()]
    return verifier

# 生成随机字符串
func _generate_random_string(length, charset):
    var result = ""
    for i in range(length):
        result += charset[randi() % charset.length()]
    return result

# 启动本地HTTP服务器
func _start_local_server():
    if http_server:
        http_server.stop()
        http_server = null
    
    http_server = TCPServer.new()
    
    # 尝试随机端口
    var port_found = false
    for _i in range(10):
        server_port = randi_range(10000, 50000) # 10000-50000
        if http_server.listen(server_port, "127.0.0.1") == OK:
            port_found = true
            break
    
    if !port_found:
        emit_signal("auth_flow_failed", "Failed to start local server")
        return
    
    redirect_uri = "http://127.0.0.1:%d/authorize" % server_port
    print("Local server started on: ", redirect_uri)
    
    # 打开浏览器进行授权
    _open_browser_for_auth()

# 打开浏览器进行授权
func _open_browser_for_auth():
    # 计算code_challenge (S256)
    var code_challenge = _calculate_code_challenge(code_verifier)
    
    var params = {
        "client_id": client_id,
        "response_type": "code",
        "redirect_uri": redirect_uri,
        "state": state,
        "code_challenge": code_challenge,
        "code_challenge_method": "S256",
        "scope": "public_profile",
        "flow": "pc_localhost"
    }
    
    var query = _encode_query(params)
    var auth_url = "%s?%s" % [AUTHORIZE_URL, query]
    
    # 在Godot中打开系统浏览器
    OS.shell_open(auth_url)

# 计算code_challenge
func _calculate_code_challenge(verifier):
    var sha256 = HashingContext.new()
    sha256.start(HashingContext.HASH_SHA256)
    sha256.update(verifier.to_utf8_buffer())
    var digest = sha256.finish()
    
    # Base64URL编码
    var base64_str = Marshalls.raw_to_base64(digest)
    base64_str = base64_str.replace("+", "-").replace("/", "_").replace("=", "")
    return base64_str

# 在_process中处理服务器连接
func _process(delta):
    if http_server and http_server.is_listening():
        # 检查新连接
        if http_server.is_connection_available():
            var client = http_server.take_connection()
            if client is StreamPeerTCP and client.get_status() == StreamPeerTCP.STATUS_CONNECTED:
                # 使用call_deferred避免阻塞主线程
                call_deferred("_handle_client_request", client)

# 处理客户端请求
func _handle_client_request(client: StreamPeerTCP):
    # 读取请求头
    var buffer = PackedByteArray()
    var header_end = false
    var end_index = -1
    var start_time = Time.get_ticks_msec()
    const TIMEOUT = 5000 # 5秒超时
    
    # 读取HTTP请求头
    while not header_end and client.get_status() == StreamPeerTCP.STATUS_CONNECTED:
        # 检查超时
        if Time.get_ticks_msec() - start_time > TIMEOUT:
            client.disconnect_from_host()
            return
            
        var bytes_available = client.get_available_bytes()
        if bytes_available > 0:
            var chunk = client.get_data(bytes_available)
            if chunk[0] == OK:
                buffer.append_array(chunk[1])
                var buffer_str = buffer.get_string_from_utf8()
                end_index = buffer_str.find("\r\n\r\n")
                if end_index != -1:
                    header_end = true
                    buffer = buffer.slice(0, end_index + 4) # 保留完整头部
        else:
            # 等待下一帧
            await get_tree().process_frame
    
    if buffer.size() == 0:
        client.disconnect_from_host()
        return
    
    # 解析请求头
    var request_text = buffer.get_string_from_utf8()
    var lines = request_text.split("\r\n")
    var request_line = lines[0] if lines.size() > 0 else ""
    
    # 只处理GET请求
    if request_line.begins_with("GET"):
        # 提取路径
        var path = request_line.split(" ")[1].split("?")[0] if request_line.split(" ").size() > 1 else ""
        
        if path == "/authorize":
            # 提取查询字符串
            var query = ""
            if "?" in request_line:
                var full_path = request_line.split(" ")[1]
                if "?" in full_path:
                    query = full_path.split("?")[1].split("#")[0] # 忽略片段标识符
                
            _handle_authorize_callback(query, client)
        else:
            _send_response(client, 404, "Not Found")
    else:
        _send_response(client, 405, "Method Not Allowed")
    
    client.disconnect_from_host()

# 发送HTTP响应
func _send_response(client: StreamPeerTCP, code: int, message: String):
    var status_text = {
        200: "OK",
        400: "Bad Request",
        404: "Not Found",
        405: "Method Not Allowed"
    }.get(code, "Unknown")
    
    var response = "HTTP/1.1 %d %s\r\n" % [code, status_text]
    response += "Content-Type: text/plain; charset=utf-8\r\n"
    response += "Content-Length: %d\r\n" % message.to_utf8_buffer().size()
    response += "Connection: close\r\n"
    response += "\r\n"
    response += message
    
    # 发送响应
    client.put_data(response.to_utf8_buffer())

# 处理授权回调
func _handle_authorize_callback(query: String, client: StreamPeerTCP):
    # 解析查询参数
    var params = {}
    for param in query.split("&"):
        var key_value = param.split("=")
        if key_value.size() == 2:
            params[key_value[0]] = key_value[1].uri_decode()
    
    # 验证state参数
    if params.get("state", "") != state:
        _send_response(client, 400, "Invalid state parameter")
        emit_signal("auth_flow_failed", "State mismatch")
        return
    
    var auth_code = params.get("code", "")
    if auth_code == "":
        _send_response(client, 400, "Missing authorization code")
        emit_signal("auth_flow_failed", "Authorization code missing")
        return
    
    # 成功响应
    _send_response(client, 200, "Authorization successful! You can close this window.")
    
    # 停止服务器
    http_server.stop()
    http_server = null
    
    # 用授权码交换令牌
    exchange_code_for_token(auth_code)


# 处理令牌响应
func _on_token_response(result, response_code, headers, body):
    if response_code != 200:
        var error = "Token request failed: %d" % response_code
        push_error(error)
        emit_signal("auth_flow_failed", error)
        return
    print(body)
    var json = JSON.parse_string(body.get_string_from_utf8())
    if json == null:
        var error = "Invalid JSON response"
        push_error(error)
        emit_signal("auth_flow_failed", error)
        return
    
    current_token_data = json # 保存token数据
    emit_signal("auth_flow_completed", json)
    
    # 自动进行后续请求
    _request_user_info(json)

# 辅助函数：URL编码表单数据
func _encode_form(data):
    var pairs = []
    for key in data:
        pairs.append("%s=%s" % [key.uri_encode(), str(data[key]).uri_encode()])
    return "&".join(pairs)

# 辅助函数：URL编码查询参数
func _encode_query(data):
    var pairs = []
    for key in data:
        pairs.append("%s=%s" % [key, str(data[key]).uri_encode()])
    return "&".join(pairs)

# =============================
# MAC签名和API请求功能
# =============================

# 请求用户信息
func _request_user_info(token_data: Dictionary):
    # 提取必要信息
    token_data = token_data["data"]
    var kid = token_data.get("kid", "")
    var mac_key = token_data.get("mac_key", "")
    var openid = token_data.get("openid", "")
    var unionid = token_data.get("unionid", "")
    
    if kid == "" or mac_key == "":
        push_error("Missing kid or mac_key in token_data")
        return
    
    # 生成MAC签名
    var host = "open.tapapis.cn"
    var request_url = "/account/basic-info/v1?client_id=" + client_id
    var mac_token = _generate_mac_token(mac_key, kid)
    
    # 准备请求头
    var headers = [
        "Authorization: " + mac_token,
        "Content-Type: application/x-www-form-urlencoded",
        "User-Agent: TapTapUnitySDK/1.2.0 UnityPlayer/2019.4.40f1c1",
        "Accept: */*",
        "Accept-Encoding: deflate, gzip",
        "Host: open.tapapis.cn",
        "X-Unity-Version: 2019.4.40f1c1"
    ]
    
    # 创建HTTP请求
    var http_request = HTTPRequest.new()
    add_child(http_request)
#    CORS代理
    var base_url = "https://" if not OS.has_feature("web") else "https://open-tapapis.bravely.pp.ua/?url="
    http_request.request(base_url + host + request_url, headers, HTTPClient.METHOD_GET)
    http_request.connect("request_completed", _on_user_info_response.bind(token_data))

# 处理用户信息响应
func _on_user_info_response(result, response_code, headers, body, token_data):
    if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
        push_error("Failed to get user info: " + str(body.get_string_from_utf8()))
        return
    
    var json = JSON.parse_string(body.get_string_from_utf8())
    if json == null:
        push_error("Invalid user info JSON")
        return
    
    emit_signal("user_info_received", json)
    
    # 创建云函数用户
    _create_cloud_user(token_data, json)

# 创建云函数用户
func _create_cloud_user(token_data: Dictionary, user_info: Dictionary):
    # 提取必要信息
    #token_data = token_data["data"]
    var kid = token_data.get("kid", "")
    var mac_key = token_data.get("mac_key", "")
    var access_token = token_data.get("access_token", "")
    var token_type = token_data.get("token_type", "")
    var mac_algorithm = token_data.get("mac_algorithm", "")
    var openid = user_info["data"].get("openid", "")
    var unionid = user_info["data"].get("unionid", "")
    
    # 构造请求体
    var auth_data = {
        "kid": kid,
        "access_token": access_token,
        "token_type": token_type,
        "mac_key": mac_key,
        "mac_algorithm": mac_algorithm,
        "openid": openid,
        "unionid": unionid
    }
    
    var request_body = {
        "authData": {
            "taptap": auth_data
        }
    }
    
    # 创建云函数URL
    var cloud_prefix = client_id.substr(0, 8).to_lower()
    var cloud_url = "https://%s.cloud.tds1.tapapis.cn/1.1/users" % cloud_prefix
    
    # 准备请求头
    var headers = [
        "Content-Type: application/json",
        "x-lc-id: " + client_id,
        "x-lc-key: " + CLIENT_KEY
    ]
    
    # 创建HTTP请求
    var http_request = HTTPRequest.new()
    add_child(http_request)
    http_request.request(cloud_url, headers, HTTPClient.METHOD_POST, JSON.stringify(request_body))
    http_request.connect("request_completed", _on_cloud_user_response)

# 处理云函数用户创建响应
func _on_cloud_user_response(result, response_code, headers, body):
    if result != HTTPRequest.RESULT_SUCCESS or response_code != 200:
        push_error("Failed to create cloud user: " + body.get_string_from_utf8())
        return
    
    var json = JSON.parse_string(body.get_string_from_utf8())
    if json == null:
        push_error("Invalid cloud user JSON")
        return
    
    emit_signal("cloud_user_created", json)

# 主MAC生成函数
func _generate_mac_token(mac_key: String, kid: String) -> String:
    # 1. 生成时间戳和随机数
    var ts = int(Time.get_unix_time_from_system())
    var nonce: int = randi() % (65536 * 65536)
    
    # 2. 构造输入字符串
    var input = "{ts}\n{nonce}\nGET\n/account/basic-info/v1?client_id={client_id}\nopen.tapapis.cn\n443\n\n".format({
        "ts": ts,
        "nonce": nonce,
        "client_id": client_id
    })
    
    # 3. 转换数据为字节数组
    var key_bytes = mac_key.to_utf8_buffer()
    var input_bytes = input.to_utf8_buffer()
    
    # 4. 计算HMAC-SHA1
    var hmac_result = _hmac_sha1_encrypt(key_bytes, input_bytes)
    
    # 5. Base64编码
    var base64_mac = Marshalls.raw_to_base64(hmac_result)
    
    # 6. 构造返回字符串
    return 'MAC id="{kid}",ts="{ts}",nonce="{nonce}",mac="{mac}"'.format({
        "kid": kid,
        "ts": ts,
        "nonce": nonce,
        "mac": base64_mac
    })

func _generate_nonce(length: int) -> String:
    var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
    var nonce = ""
    for i in range(length):
        nonce += chars[randi() % chars.length()]
    return nonce

# 清理资源
func _exit_tree():
    if http_server and http_server.is_listening():
        http_server.stop()
    if polling_timer:
        polling_timer.stop()


func _hmac_sha1_encrypt(key: PackedByteArray, data: PackedByteArray) -> PackedByteArray:
    # HMAC-SHA1 implementation
    var block_size = 64 # SHA-1 block size in bytes
    var output_size = 20 # SHA-1 output size in bytes
    
    # Keys longer than blockSize are shortened by hashing them
    var actual_key = key.duplicate()
    if actual_key.size() > block_size:
        actual_key = _sha1(actual_key)
    
    # Keys shorter than blockSize are padded to blockSize by padding with zeros
    if actual_key.size() < block_size:
        actual_key.resize(block_size)
        for i in range(key.size(), block_size):
            actual_key[i] = 0
    
    # Create inner and outer padding
    var ipad = PackedByteArray()
    var opad = PackedByteArray()
    
    ipad.resize(block_size)
    opad.resize(block_size)
    
    for i in range(block_size):
        ipad[i] = 0x36
        opad[i] = 0x5C
    
    # XOR key with ipad and opad
    var inner_key = _xor_arrays(actual_key, ipad)
    var outer_key = _xor_arrays(actual_key, opad)
    
    # Perform inner hash: SHA1(key XOR ipad || message)
    var inner_data = inner_key + data
    var inner_hash = _sha1(inner_data)
    
    # Perform outer hash: SHA1(key XOR opad || inner_hash)
    var outer_data = outer_key + inner_hash
    var result = _sha1(outer_data)
    
    return result

func _sha1(data: PackedByteArray) -> PackedByteArray:
    var h = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0]
    
    # Pre-processing: adding padding bits
    var msg = data.duplicate()
    var msg_len = msg.size()
    var bit_len = msg_len * 8
    
    # Append the '1' bit (plus zero padding to make it a byte)
    msg.append(0x80)
    
    # Append 0 <= k < 512 bits '0', such that resulting message length is congruent to 448 ≡ -64 (mod 512)
    while (msg.size() % 64) != 56:
        msg.append(0x00)
    
    # Append length of message (before pre-processing), in bits, as 64-bit big-endian integer
    for i in range(8):
        msg.append((bit_len >> (8 * (7 - i))) & 0xFF)
    
    # Process the message in 512-bit chunks
    for chunk_start in range(0, msg.size(), 64):
        var w = []
        w.resize(80)
        
        # Break chunk into sixteen 32-bit big-endian words
        for i in range(16):
            var word = 0
            for j in range(4):
                word = (word << 8) | msg[chunk_start + i * 4 + j]
            w[i] = word
        
        # Extend the sixteen 32-bit words into eighty 32-bit words
        for i in range(16, 80):
            var temp = w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16]
            w[i] = _rotl(temp, 1)
        
        # Initialize hash value for this chunk
        var a = h[0]
        var b = h[1]
        var c = h[2]
        var d = h[3]
        var e = h[4]
        
        # Main loop
        for i in range(80):
            var f: int
            var k: int
            
            if i < 20:
                f = (b & c) | (~b & d)
                k = 0x5A827999
            elif i < 40:
                f = b ^ c ^ d
                k = 0x6ED9EBA1
            elif i < 60:
                f = (b & c) | (b & d) | (c & d)
                k = 0x8F1BBCDC
            else:
                f = b ^ c ^ d
                k = 0xCA62C1D6
            
            var temp = (_rotl(a, 5) + f + e + k + w[i]) & 0xFFFFFFFF
            e = d
            d = c
            c = _rotl(b, 30)
            b = a
            a = temp
        
        # Add this chunk's hash to result so far
        h[0] = (h[0] + a) & 0xFFFFFFFF
        h[1] = (h[1] + b) & 0xFFFFFFFF
        h[2] = (h[2] + c) & 0xFFFFFFFF
        h[3] = (h[3] + d) & 0xFFFFFFFF
        h[4] = (h[4] + e) & 0xFFFFFFFF
    
    # Convert hash to bytes
    var result = PackedByteArray()
    for i in range(5):
        for j in range(4):
            result.append((h[i] >> (8 * (3 - j))) & 0xFF)
    
    return result
    
# Left rotate function
func _rotl(value: int, amount: int) -> int:
    return ((value << amount) | (value >> (32 - amount))) & 0xFFFFFFFF
    
# XOR two PackedByteArrays
func _xor_arrays(a: PackedByteArray, b: PackedByteArray) -> PackedByteArray:
    var result = PackedByteArray()
    var min_len = min(a.size(), b.size())
    for i in range(min_len):
        result.append(a[i] ^ b[i])
    return result
