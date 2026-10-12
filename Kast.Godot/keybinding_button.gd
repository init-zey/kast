extends Button
# 打开 keychain 键位编辑器；节点在游戏场景里被移到了暂停菜单下：Game/PauseMenu/Keybinding
func _pressed() -> void:
	$"../../Keybinding".popup_centered_ratio(0.9)
