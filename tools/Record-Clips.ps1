<#
.SYNOPSIS
Records a card game's tutorial clips into tutorial/<name>.ogv, square, about a second or two each
side of the moment they show.

.EXAMPLE
powershell -File addons/renegade_cards/tools/Record-Clips.ps1 battle war:3

.DESCRIPTION
Each clip is `name` or `name:players`. The game runs with `-- --clip=<name>`: the computer plays
every seat at high speed under a magenta cover until the game reaches that moment, then plays it
at normal speed and quits (CardGame.ClipMoment). Every frame is written as a PNG; the covered
frames are dropped, the rest cropped to a square around the table (as wide as the frame,
-Top down it) and played back through tools/play_frames.gd into Godot's movie writer, which
makes the Ogg Theora clip.
#>
param(
	[Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)] [string[]] $Clips,
	[int] $Seed = 5,
	# Where the square starts down the frame (a fraction of its height): it's as wide as the frame.
	[double] $Top = 0.275,
	# Keep the frames (in clip-frames in the temp folder) to look at.
	[switch] $KeepFrames
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$project = Get-Location
$godot = Join-Path $project '..\Godot.exe' | Resolve-Path
New-Item -ItemType Directory -Force (Join-Path $project 'tutorial') | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) "clips-$([guid]::NewGuid().ToString('N'))"

function Test-Cover([string] $file) {
	$image = [System.Drawing.Bitmap]::FromFile($file)
	try {
		$p = $image.GetPixel([int]($image.Width / 2), [int]($image.Height / 2))
		return $p.R -gt 200 -and $p.G -lt 60 -and $p.B -gt 200
	} finally { $image.Dispose() }
}

try {
	foreach ($spec in $Clips) {
		$name, $players = $spec -split ':'
		$frames = Join-Path $work $name
		$keep = Join-Path $work "$name-keep"
		New-Item -ItemType Directory -Force $frames, $keep | Out-Null
		$arguments = @('--path', "`"$project`"", '--write-movie', "`"$frames\f.png`"", '--fixed-fps', '30', '--', "--clip=$name", "--seed=$Seed")
		if ($players) { $arguments += "--players=$players" }
		$run = Start-Process -FilePath $godot -ArgumentList $arguments -NoNewWindow -PassThru
		if (-not $run.WaitForExit(900000)) { $run.Kill(); Write-Warning "$name took over 15 minutes and was stopped"; continue }
		# Drop everything up to the last covered frame (the first few are drawn before the cover is up).
		$all = @(Get-ChildItem $frames -Filter *.png | Sort-Object Name)
		$start = 0
		while ($start -lt $all.Count -and -not (Test-Cover $all[$start].FullName)) { $start++ }
		while ($start -lt $all.Count -and (Test-Cover $all[$start].FullName)) { $start++ }
		if ($start -ge $all.Count) { Write-Warning "$name never reached its moment"; continue }
		$i = 0
		foreach ($file in $all[$start..($all.Count - 1)]) {
			Move-Item $file.FullName (Join-Path $keep ('k{0:D6}.png' -f $i))
			$i++
		}
		$out = Join-Path $project "tutorial\$name.ogv"
		$first = [System.Drawing.Image]::FromFile((Get-ChildItem $keep -Filter *.png | Select-Object -First 1).FullName)
		$size = $first.Width
		$crop = @(0, [int]($first.Height * $Top), $size)
		$first.Dispose()
		$override = Join-Path $project 'override.cfg'
		@"
[display]
window/size/window_width_override=$size
window/size/window_height_override=$size
window/size/viewport_width=$size
window/size/viewport_height=$size
"@ | Set-Content -Encoding ascii $override
		try {
			$play = Start-Process -FilePath $godot -ArgumentList @('--path', "`"$project`"", '--write-movie', "`"$out`"", '--fixed-fps', '30', '-s', 'res://addons/renegade_cards/tools/play_frames.gd', '--', "--frames=$keep", "--crop=$($crop -join ',')") -NoNewWindow -PassThru
			$play.WaitForExit(600000) | Out-Null
		} finally { Remove-Item $override }
		Write-Host "Recorded $out ($i frames, $([math]::Round((Get-Item $out).Length / 1KB)) KB)"
		if ($KeepFrames) {
			$kept = Join-Path ([IO.Path]::GetTempPath()) "clip-frames\$(Split-Path $project -Leaf)-$name"
			Remove-Item -Recurse -Force $kept -ErrorAction SilentlyContinue
			Copy-Item -Recurse $keep $kept
			Write-Host "Frames kept in $kept"
		}
	}
} finally {
	Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
