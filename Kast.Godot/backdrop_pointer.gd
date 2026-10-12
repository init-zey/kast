extends Node3D
# 让本节点跟随鼠标：把鼠标屏幕坐标投影成一条射线，与 XY 平面求交作为位置。
# 用于背景装饰物贴着鼠标移动，本身不参与物理。
# 注意依赖 @export 的 camera 已连好，否则 intersects_ray 会因 camera 为 null 报错。

@export var camera:Camera3D

func _input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		position = Plane.PLANE_XY.intersects_ray(camera.global_position,camera.project_ray_normal(event.position))
