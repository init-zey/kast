shader_type canvas_item;

uniform sampler2D mask : hint_default_white, filter_linear;
uniform vec2 p0;
uniform float val;

void fragment() {
	COLOR.rgb=vec3(0);
	COLOR.a=texture(mask,((SCREEN_UV-p0*SCREEN_PIXEL_SIZE)/SCREEN_PIXEL_SIZE/(val*1500.0)+vec2(0.5))).r;
}