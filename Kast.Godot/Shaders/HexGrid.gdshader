shader_type canvas_item;

// 前景色（六边形内部）
uniform vec4 fg_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
// 背景色（六边形外部）
uniform vec4 bg_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
// 节点尺寸（像素），例如 (1024, 768)
uniform vec2 node_size = vec2(1024.0, 768.0);
// 整体偏移量（像素）
uniform vec2 offset = vec2(0.0, 0.0);
// 六边形外接圆半径（像素）
uniform float hex_size = 20.0;

const float SQRT3 = 1.7320508075688772;

// 轴向坐标 (q, r) 转像素坐标（尖顶六边形）
vec2 hex_to_pixel(vec2 hex, float size) {
    float x = size * SQRT3 * (hex.x + hex.y * 0.5);
    float y = size * 1.5 * hex.y;
    return vec2(x, y);
}

// 像素坐标转轴向坐标（尖顶六边形）
vec2 pixel_to_hex(vec2 p, float size) {
    float q = (SQRT3 / 3.0 * p.x - 1.0 / 3.0 * p.y) / size;
    float r = (2.0 / 3.0 * p.y) / size;
    return vec2(q, r);
}

// 轴向坐标舍入到最近的六边形中心
vec2 hex_round(vec2 hex) {
    float q = hex.x;
    float r = hex.y;
    float s = -q - r;
    float rq = round(q);
    float rr = round(r);
    float rs = round(s);
    float dq = abs(rq - q);
    float dr = abs(rr - r);
    float ds = abs(rs - s);
    if (dq > dr && dq > ds) {
        rq = -rr - rs;
    } else if (dr > ds) {
        rr = -rq - rs;
    } else {
        rs = -rq - rr;
    }
    return vec2(rq, rr);
}

void fragment() {
    // 把 UV 映射到节点像素坐标，再加上偏移量
    vec2 p = UV * node_size + offset;

    // 像素坐标 -> 轴向坐标
    vec2 hex = pixel_to_hex(p, hex_size);
    // 舍入到最近的六边形中心
    vec2 center_hex = hex_round(hex);
    // 轴向坐标 -> 中心像素坐标
    vec2 center_pixel = hex_to_pixel(center_hex, hex_size);

    // 当前像素到最近六边形中心的相对位置
    vec2 d = p - center_pixel;

    // 判断是否在尖顶六边形内部
    float R = hex_size;
    bool inside = (abs(d.x)+abs(d.y))<2.0;

    vec4 color = inside ? fg_color : bg_color;
    COLOR = color;
}