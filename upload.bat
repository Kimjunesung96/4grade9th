@echo off
chcp 65001
cls
echo ?? [시스템] 히스토리 보존형 자동 깃 업로드 공법을 시작합니다...
echo.

:: 1. 임시 작업 폴더 생성 및 이동
cd /d C:\Users\skrkt\Desktop
if exist temp_clone (
    echo ?? 기존 임시 폴더가 남아있어 청소합니다...
    rmdir /s /q temp_clone
)
mkdir temp_clone
cd temp_clone

:: 2. 깃허브 원격 장부 클론 (히스토리 유지 핵심)
echo ?? [1/4] 깃허브에서 기존 히스토리 장부를 땡겨오는 중...
git clone https://github.com/Kimjunesung96/4grade9th.git .
if %errorlevel% neq 0 goto ERROR

:: 3. cs2_코드모음 폴더에서 새 파일들만 복사 (숨겨진 .git 폴더는 제외하여 충돌 차단)
echo ?? [2/4] cs2_코드모음에서 최신 파일 복사 중...
xcopy "C:\Users\skrkt\Desktop\cs2_코드모음" "C:\Users\skrkt\Desktop\temp_clone" /E /H /Y /C /EXCLUDE:exclude_list.txt >nul 2>&1
:: 혹시 cs2_코드모음에 숨은 .git이 있을까봐 안전하게 한 번 더 제거
if exist .git_tar rmdir /s /q .git_tar

:: 4. 깃 장바구니 담기 및 커밋
echo ?? [3/4] 새 파일 장부 기록(Commit) 중...
git add .
git commit -m "feat: cs2_코드모음 파일 자동 업데이트"

:: 5. 원격 저장소로 발사 (Push)
echo ?? [4/4] 깃허브 본사로 최종 장부 전송(Push) 중...
git push origin main
if %errorlevel% neq 0 (
    echo.
    echo ?? 메인 브랜치 전송 실패. master 브랜치로 재시도합니다...
    git push origin master
)

if %errorlevel% neq 0 goto ERROR

:: 6. 마무리 청소
cd ..
rmdir /s /q temp_clone
echo.
echo ========================================================
echo ?? [성공] 히스토리 유실 없이 모든 파일이 깃허브에 안착했습니다!
echo ========================================================
goto END

:ERROR
echo.
echo ? [에러 대참사] 깃 전송 중 문제가 발생했습니다. 네트워크나 권한을 확인하세요.
cd /d C:\Users\skrkt\Desktop
if exist temp_clone rmdir /s /q temp_clone

:END
pause