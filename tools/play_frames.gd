extends SceneTree
## Plays a folder of PNG frames back, one per frame, cropped to a square, so Godot's movie writer
## can turn them into a tutorial clip (tools/Record-Clips.ps1 runs it). Arguments after `--`:
## --frames=<folder> --crop=<x>,<y>,<size> (in the frames' pixels).

var _files: Array[String] = []
var _index := 0
var _rect: TextureRect
var _crop := Rect2()


func _initialize() -> void:
	var folder := ""
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--frames="):
			folder = arg.substr(9)
		elif arg.begins_with("--crop="):
			var parts := arg.substr(7).split(",")
			_crop = Rect2(float(parts[0]), float(parts[1]), float(parts[2]), float(parts[2]))
	for name in DirAccess.get_files_at(folder):
		if name.ends_with(".png"):
			_files.append(folder.path_join(name))
	_files.sort()
	_rect = TextureRect.new()
	_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_rect.stretch_mode = TextureRect.STRETCH_SCALE
	root.add_child(_rect)


func _process(_delta: float) -> bool:
	if _index >= _files.size():
		return true
	var image := Image.load_from_file(_files[_index])
	_index += 1
	if _crop.size.x > 0:
		image = image.get_region(Rect2i(_crop))
	_rect.texture = ImageTexture.create_from_image(image)
	return false
