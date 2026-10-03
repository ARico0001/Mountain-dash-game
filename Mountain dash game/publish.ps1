param(
	[string]$Configuration = "Release",
	[string]$Runtime = "win-x64",
	[switch]$SelfContained
)

$proj = "Mountain dash game/Mountain dash game.csproj"

if ($SelfContained) {
	Write-Host "Publishing self-contained single-file (Runtime: $Runtime, Configuration: $Configuration)"
	dotnet publish "$proj" -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true -o .\publish\sc
} else {
	Write-Host "Publishing framework-dependent (Runtime: $Runtime, Configuration: $Configuration)"
	dotnet publish "$proj" -c $Configuration -r $Runtime --self-contained false -o .\publish\fd
}

Write-Host "Done. Output folders: .\publish\sc and .\publish\fd"
