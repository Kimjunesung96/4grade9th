import os
import re
from collections import defaultdict

# 탐색할 최상위 경로 (유니티 프로젝트의 Assets 폴더로 지정하시면 좋습니다)
BASE_DIR = '.'
# 유니티/C# 환경에 맞춰 스캔을 무시할 무거운 시스템 폴더들
IGNORE_DIRS = {'Library', 'Logs', 'Packages', 'ProjectSettings', 'obj', 'bin', '.git', '.vs'}

forward_graph = defaultdict(set)
reverse_graph = defaultdict(set)

def scan_project():
    print("🔍 C# 프로젝트 코드를 스캔하여 관계 지도를 그리는 중입니다...\n")
    
    # 1. 모든 .cs 파일 찾기 및 클래스명(파일명) 추출
    all_files = {}
    for root, dirs, files in os.walk(BASE_DIR):
        dirs[:] = [d for d in dirs if d not in IGNORE_DIRS]
        for file in files:
            if file.endswith('.cs'):
                full_path = os.path.normpath(os.path.join(root, file))
                # 유니티 C# 특성상 파일명이 곧 메인 클래스/구조체 이름입니다.
                class_name = file.replace('.cs', '')
                all_files[full_path] = class_name

    # 2. 각 파일 내용을 읽어 다른 파일명(클래스명)이 코드에 쓰였는지 검사
    for file_path, current_class in all_files.items():
        try:
            with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                content = f.read()
        except Exception:
            continue
            
        # 파일 내용을 단어 단위로 쪼개어 Set(집합)으로 만듦 -> 검색 속도 비약적 향상
        content_words = set(re.findall(r'\b\w+\b', content))
        
        for target_path, target_class in all_files.items():
            if file_path == target_path:
                continue # 자기 자신은 제외
            
            # 다른 클래스명이 현재 파일의 단어 목록에 있다면 참조(의존성)하는 것으로 판정
            if target_class in content_words:
                forward_graph[file_path].add(target_path)
                reverse_graph[target_path].add(file_path)

    print(f"✅ 스캔 완료! 총 {len(all_files)}개의 코드를 분석하여 지도를 완성했습니다.")

def search_dependencies():
    while True:
        print("=" * 60)
        query = input("💡 검색할 파일명/클래스명 입력 (예: BlockStress) / 종료는 q: ").strip()
        
        if query.lower() == 'q':
            print("🚀 프로그램을 종료합니다. 커피 한 잔 하시면서 쉬세요!")
            break
            
        if not query:
            continue
            
        # 검색어와 부분 일치하는 파일 찾기
        found_files = [f for f in forward_graph.keys() | reverse_graph.keys() if query.lower() in os.path.basename(f).lower()]
        
        if not found_files:
            print(f"❌ '{query}'(이)가 포함된 코드를 찾을 수 없거나, 연결된 의존성이 없습니다.")
            continue
            
        for target in found_files:
            print(f"\n📂 [선택된 파일]: {target}")
            
            print("\n  [⬇️ 이 코드가 가져다 쓰는 스크립트 (References)]")
            if forward_graph[target]:
                for dep in sorted(forward_graph[target]):
                    print(f"    - {dep}")
            else:
                print("    (없음)")
                
            print("\n  [⬆️ 이 코드를 뼈대로 쓰는 스크립트 (Used By)]")
            if reverse_graph[target]:
                for dep in sorted(reverse_graph[target]):
                    print(f"    - {dep}")
            else:
                print("    (없음)")

if __name__ == "__main__":
    scan_project()
    search_dependencies()