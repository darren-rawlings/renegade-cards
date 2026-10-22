<#
.SYNOPSIS
  Creates a new Renegade card game: its GitHub repo (from renegade-game-template), the name, and
  the card kit linked in beside the studio core. Safe to run again to finish a half-done setup.

.EXAMPLE
  powershell -File C:\GameDev\renegade-cards\tools\New-CardGame.ps1 -Title "Old Maid" -CodeName OldMaid -PackageId oldmaid
#>
param(
	[Parameter(Mandatory)][string]$Title,
	[Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]*$')][string]$CodeName,
	[Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9]*$')][string]$PackageId,
	[string]$Root = 'C:\GameDev\Godot and VS Code'
)
# git and gh write progress to stderr, which Windows PowerShell would treat as fatal under 'Stop'.
$ErrorActionPreference = 'Continue'
$dir = Join-Path $Root $CodeName
function Cloned { (Test-Path (Join-Path $dir 'GameTemplate.csproj')) -or (Test-Path (Join-Path $dir "$CodeName.csproj")) }

if (-not (Cloned)) {
	if (Test-Path $dir) { throw "$dir exists but isn't a game folder" }
	Push-Location $Root
	try {
		# GitHub refuses template copies made too quickly one after another: retry a few times.
		for ($try = 1; $try -le 4 -and -not (Cloned); $try++) {
			gh repo view "darren-rawlings/$CodeName" *> $null
			if ($LASTEXITCODE -ne 0) {
				gh repo create "darren-rawlings/$CodeName" --public --template darren-rawlings/renegade-game-template 2>&1 | Out-Host
				Start-Sleep -Seconds 6
			}
			git clone "https://github.com/darren-rawlings/$CodeName" $CodeName 2>&1 | Out-Host
			if (-not (Cloned)) {
				if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
				Start-Sleep -Seconds (15 * $try)
			}
		}
	}
	finally { Pop-Location }
	if (-not (Cloned)) { throw "Couldn't create $CodeName from the template" }
}

Push-Location $dir
try {
	git submodule update --init 2>&1 | Out-Null
	# The studio core at its newest, and the card kit beside it.
	git -C addons/renegade_core fetch -q origin main 2>&1 | Out-Null
	git -C addons/renegade_core checkout -q origin/main 2>&1 | Out-Null
	if (-not (Test-Path addons/renegade_cards/pure)) {
		git submodule add https://github.com/darren-rawlings/renegade-cards addons/renegade_cards 2>&1 | Out-Null
	}
	if (Test-Path 'GameTemplate.csproj') {
		powershell -NoProfile -File tools/New-Game.ps1 -Title $Title -CodeName $CodeName -PackageId $PackageId | Out-Null
	}

	$utf8 = New-Object System.Text.UTF8Encoding $false
	$proj = Join-Path $dir "$CodeName.csproj"
	$text = [IO.File]::ReadAllText($proj)
	if ($text -notmatch 'renegade_cards') {
		$text = $text.Replace('addons/renegade_core/**/obj/**</DefaultItemExcludes>', 'addons/renegade_core/**/obj/**;addons/renegade_cards/tests/**;addons/renegade_cards/tools/**;addons/renegade_cards/**/bin/**;addons/renegade_cards/**/obj/**</DefaultItemExcludes>')
		[IO.File]::WriteAllText($proj, $text, $utf8)
	}
	$tests = Join-Path $dir "tests\$CodeName.Tests\$CodeName.Tests.csproj"
	$text = [IO.File]::ReadAllText($tests)
	if ($text -notmatch 'renegade_cards') {
		$text = $text.Replace('Link="Shared/%(RecursiveDir)%(Filename)%(Extension)" />', "Link=`"Shared/%(RecursiveDir)%(Filename)%(Extension)`" />`r`n    <!-- The card kit's engine-free code (Card, Deck). -->`r`n    <Compile Include=`"../../addons/renegade_cards/pure/**/*.cs`" Link=`"Cards/%(RecursiveDir)%(Filename)%(Extension)`" />")
		[IO.File]::WriteAllText($tests, $text, $utf8)
	}
	Remove-Item (Join-Path $dir 'src\Core\Counter.cs*'), (Join-Path $dir "tests\$CodeName.Tests\CounterTests.cs*") -ErrorAction SilentlyContinue
	Write-Host "Ready: $dir (com.renegadegames.$PackageId), with addons/renegade_cards."
}
finally { Pop-Location }
