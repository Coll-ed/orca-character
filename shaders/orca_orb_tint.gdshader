shader_type canvas_item;

// 银龙奥卡——能量美术换色（我们自己的 shader，灵感来自"按亮度重映射"这一常见做法）。
// 作用：把原图（战士那套红色/橙色能量）按**亮度**在「暗色 → 亮色」两个色之间重映射，
//       于是同一套素材能出：板甲＝灰黑、婚纱＝红白；alpha 与 modulate 都保留
//       （游戏在没能量时会用 Layers.Modulate 压暗，那一步仍然有效）。
uniform vec4 color_dark : source_color = vec4(0.06, 0.06, 0.07, 1.0);
uniform vec4 color_bright : source_color = vec4(0.62, 0.63, 0.67, 1.0);
uniform float mid_gamma : hint_range(0.2, 3.0) = 1.0;
uniform float lum_boost : hint_range(0.1, 4.0) = 1.0;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    float lum = clamp(dot(tex.rgb, vec3(0.299, 0.587, 0.114)) * lum_boost, 0.0, 1.0);
    lum = pow(lum, mid_gamma);
    COLOR = vec4(mix(color_dark.rgb, color_bright.rgb, lum), tex.a) * COLOR;
}
