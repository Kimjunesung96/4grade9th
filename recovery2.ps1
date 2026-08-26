$SourceFolder  = "C:\Users\skrkt\Desktop\cs2_code"
$TargetRepoUrl = "https://github.com/Kimjunesung96/4grade9th.git"
$CloneFolder   = "C:\Users\skrkt\Desktop\git_recovery_cloned"

if (Test-Path $CloneFolder) { Remove-Item -Recurse -Force $CloneFolder }
git clone $TargetRepoUrl $CloneFolder
Set-Location $CloneFolder

$SubFolders = Get-ChildItem -Path $SourceFolder -Directory |
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

    # 🌟 핵심 1: 복사하기 전에 기존 깃 폴더 내부를 싹 비움 (.git 폴더는 깃허브 장부이므로 절대 제외)
    Get-ChildItem -Path $CloneFolder -Exclude .git | Remove-Item -Recurse -Force

    # 🌟 핵심 2: 새 날짜의 폴더를 복사 (이렇게 해야 삭제된 파일도 완벽하게 반영됨)
    Copy-Item -Path "$($Folder.FullName)\*" -Destination $CloneFolder -Recurse -Force

    $env:GIT_COMMITTER_DATE = $GitDate
    $env:GIT_AUTHOR_DATE    = $GitDate

    git add --all # 변경, 추가, 삭제된 모든 내역을 장바구니에 담음
    $st = git status --porcelain
    
    $msg = "feat: $Year-$Month-$Day work"
    
    if ($st) {
        git commit --date="$GitDate" -m $msg
        Write-Host "  committed" -ForegroundColor Green
    } else {
        # 🌟 핵심 3: 코드가 똑같아도 억지로 잔디를 심기 위해 --allow-empty 옵션 사용
        git commit --allow-empty --date="$GitDate" -m $msg
        Write-Host "  force committed (잔디 강제 심기 완료)" -ForegroundColor Yellow
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