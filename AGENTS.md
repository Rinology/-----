# ChatHelper 에이전트 작업 규칙 (AGENTS.md)

이 프로젝트(`c:\Users\taerin\Desktop\채팅도우미`)에서 코드 수정, 기능 추가, 버그 수정, 리팩토링 등 **패치(Patch)나 빌드 배포(`publish`)가 발생할 때마다 에이전트는 반드시 아래 규칙을 준수**해야 합니다.

## 1. 패치 시 필수 문서 업데이트 규칙 (Mandatory on Every Patch)

1. **`PATCH_NOTES.md` (패치노트) 작성 및 갱신**
   - 코드 변경이나 `publish` 빌드가 완료될 때마다 루트의 [PATCH_NOTES.md](file:///C:/Users/taerin/Desktop/채팅도우미/PATCH_NOTES.md) 최상단에 최신 날짜/버전 항목을 추가하거나 당일 패치 항목에 변경 내역을 기록합니다.
   - 기록 항목:
     - **추가/개선된 기능 (Added / Changed)**
     - **수정된 버그 (Fixed)**
     - **제거/경량화된 항목 (Removed)**
     - **GAS 재배포 필요 여부** (`GAS/Code.gs` 수정 시 명시)

2. **`README.md` (리드미) 최신 상태 유지**
   - 기능이 추가·변경·제거될 때마다 루트의 [README.md](file:///C:/Users/taerin/Desktop/채팅도우미/README.md)에 적힌 주요 기능, 단축키(`Alt+Q`, `Alt+X` 등), 아키텍처(GAS 연동 등), 실행 및 세팅 안내가 실제 코드와 100% 일치하도록 즉시 함께 업데이트합니다.
   - 이미 제거되거나 바뀐 기능(예: 구버전 설명)이 `README.md`에 남아있지 않도록 점검합니다.

3. **`docs/BASELINE_STATUS.md` (회귀 기준선 문서) 동기화**
   - 핵심 동작이나 정책이 바뀌면 [docs/BASELINE_STATUS.md](file:///C:/Users/taerin/Desktop/채팅도우미/docs/BASELINE_STATUS.md)의 체크리스트도 함께 갱신합니다.

## 2. 코드 수정 전 필수 확인 규칙 (실패 이력 참조)

- **작업 시작 전 [docs/LESSONS_LEARNED.md](file:///C:/Users/taerin/Desktop/채팅도우미/docs/LESSONS_LEARNED.md) 확인**:
  - 코드를 수정하거나 버그를 해결하기 전에 반드시 [docs/LESSONS_LEARNED.md](file:///C:/Users/taerin/Desktop/채팅도우미/docs/LESSONS_LEARNED.md)를 먼저 읽고, 이미 실패한 접근법(❌)을 되풀이하지 않도록 다른 검증된 방법을 선택합니다.
- **새로운 실패/시행착오 발생 시 즉시 기록**:
  - 작업 중 특정 접근 방식이 환경·Win32·WPF·IME·GAS 제약으로 실패하여 다른 방식으로 우회/해결했다면, 그 원인과 해결책을 `docs/LESSONS_LEARNED.md`에 추가해 다음 작업에서도 같은 실수를 반복하지 않도록 합니다.
