$SourceFolder  = "C:\Users\skrkt\Desktop\cs2_code"
$TargetRepoUrl = "https://github.com/Kimjunesung96/4grade9th.git"
$CloneFolder   = "C:\Users\skrkt\Desktop\git_recovery_cloned"

if (Test-Path $CloneFolder) { Remove-Item -Recurse -Force $CloneFolder }
git clone $TargetRepoUrl $CloneFolder
Set-Location $CloneFolder

$SubFolders = Get-ChildItem -Path "C:\Users\skrkt\Desktop\cs2_code" -Directory |
              Where-Object { $_.Name -match '^\d{8}_\d{6}$' } |
              Sort-Object Name

Write-Host "found: $($SubFolders.Count) folders" -ForegroundColor Cyan

foreach ($Folder in $SubFolders) {
    $N = $Folder.Name
    $Year  = $N.Substring(0,4)
    $Month = $N.Substring(4,2)
    $Day   = $N.Substring(6,2)
    $Hour  = $N.Substring(9,2)
    $Min   = $N.Substring(11,2)
    $Sec   = $N.Substring(13,2)
    $GitDate = "$Year-$Month-$Day $Hour`:$Min`:$Sec"

    Write-Host "processing $GitDate" -ForegroundColor Cyan

    Copy-Item -Path "$($Folder.FullName)\*" -Destination $CloneFolder -Recurse -Force

    $env:GIT_COMMITTER_DATE = $GitDate
    $env:GIT_AUTHOR_DATE    = $GitDate

    git add .
    $st = git status --porcelain
    if ($st) {
        $msg = "feat: $Year-$Month-$Day work"
        git commit --date="$GitDate" -m $msg
        Write-Host "  committed" -ForegroundColor Green
    } else {
        Write-Host "  no changes, skip" -ForegroundColor Yellow
    }
}

Remove-Item env:GIT_COMMITTER_DATE -ErrorAction SilentlyContinue
Remove-Item env:GIT_AUTHOR_DATE    -ErrorAction SilentlyContinue

git push origin main
if ($LASTEXITCODE -ne 0) { git push origin master }

if ($LASTEXITCODE -eq 0) {
    Write-Host "done!" -ForegroundColor Green
} else {
    Write-Host "push failed" -ForegroundColor Red
}
