shader_type canvas_item;

// 移除屏幕纹理相关声明
/**
* How blurry the result should be.
* Limited to 20 because of performance, if you want feel free to break it.
*/
uniform float strength : hint_range(0.1, 5.0) = 3.3;
/**
* How dark the blur will be
*/
uniform float mix_percentage: hint_range(0.0, 1.0) = 0.3;


float gaussianDistribution(float x, float STD) { // STD stands for standard deviation
	return exp(-(x*x)/(2.*STD*STD))/(sqrt(2.*PI)*STD);
}

vec3 gaussianblur(sampler2D sampler, vec2 pos, vec2 pixel_size, float sigmaUsed, int radius) {
    vec3 blurredPixel = vec3(0.0);
    float total_weight = 0.0;

    // 使用当前节点的纹理而不是屏幕纹理
    for(int i = -radius; i <= radius; i++) {
        for(int j = -radius; j <= radius; j++) {
            // 计算偏移
            vec2 offset = vec2(float(i), float(j)) * pixel_size;
            vec2 sample_uv = pos + offset;

            // 确保采样在有效范围内（可选）
            if (sample_uv.x >= 0.0 && sample_uv.x <= 1.0 && sample_uv.y >= 0.0 && sample_uv.y <= 1.0) {
                float weight = gaussianDistribution(float(i), sigmaUsed) * gaussianDistribution(float(j), sigmaUsed);
                blurredPixel += texture(sampler, sample_uv).rgb * weight;
                total_weight += weight;
            }
        }
    }

    // 避免除零错误
    if (total_weight > 0.0) {
        blurredPixel /= total_weight;
    }
    return blurredPixel;
}

void fragment() {
    // 使用当前节点的纹理和UV坐标
    vec3 PixelBlurred = gaussianblur(TEXTURE, UV, TEXTURE_PIXEL_SIZE, strength, int(round(3.0 * strength)));

    // 混合原始颜色（可选保留）
    vec3 original_color = texture(TEXTURE, UV).rgb;
    vec3 color = mix(PixelBlurred, vec3(0.0), mix_percentage);

    COLOR = vec4(color, texture(TEXTURE, UV).a); // 保留原始alpha通道
}