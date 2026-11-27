# 📋 ChatHelper v3.3.0 개선 및 버그 수정 계획서 (`docs/PLAN_v3.3.md`)

> 작성일: 2026-11-27  
> 목적: 추후 `v3.3.0` 패치 시 그대로 반영할 수 있도록 현재 확인된 버그 3건과 UX 개선 3건의 원인·구현 설계를 정리한 문서입니다.

---

## 1. `챗헬프 임시메모` ↔ `Xenie` 자석 도킹 분리 버그 수정

### 🔍 현상 (스크린샷 확인)
- `챗헬프 임시메모`와 `Xenie`가 붙어 있을 때, `Xenie`의 `🔓 임시메모 분리`를 눌러야만 떨어지고 **`챗헬프 임시메모` 창을 드래그하거나 `🔓` 버튼을 눌렀을 때 제대로 떨어지지 않고 다시 붙는 현상**이 발생.

### 🔍 원인
- [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs)의 `OnStickyMemoLocationChanged`에서 `_isMagneticDocked == true`일 때 `챗헬프 임시메모`를 움직이면 `Xenie`를 무조건 따라 움직이게 되어 있고, 드래그로 떼어내는(Tear-off) 판별이나 분리 후 재스냅 방지 쿨다운이 없음.
- 또한 [StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml)의 도킹 토글 버튼이 작은 `🔗`/`🔓` 아이콘만 있어 상태 인지가 어렵고, 드래그 시작 시 `DragMove()`와 `LocationChanged`가 연속 발생하면서 22px 이내에서 즉시 재도킹됨.

### ✅ 구현 계획
1. **분리 후 재스냅 쿨다운(`_dockCooldownUntil`) 도입**:
   - `ToggleMagneticDock()`으로 분리하거나 사용자가 창을 떼어낼 때 **1.5초간 자동 자석 스냅을 일시 중지**해 의도치 않게 다시 달라붙는 현상 방지.
2. **임시메모 창에서도 직관적 분리/붙이기 버튼 제공**:
   - `StickyMemoWindow` 헤더의 `🔗`/`🔓` 버튼을 **`🔗 붙이기` / `🔓 분리`** 텍스트 포함 버튼으로 명확히 표시.
3. **창 표시/숨김 시 도킹 상태 안전 동기화**:
   - 한쪽 창을 닫거나(`Hide`) 다시 열 때 `_isMagneticDocked` 상태가 꼬이지 않도록 `IsVisibleChanged`에서 도킹 정렬·해제 상태를 일관되게 유지.

---

## 2. `챗헬프 임시메모`의 `[입력]` 버튼 클릭 시 채팅창에 입력되지 않는 문제

### 🔍 현상
- `챗헬프 임시메모` 카드 우측의 **`입력`** 버튼(또는 더블클릭)을 눌러도 대상 채팅창에 텍스트가 입력되지 않음.

### 🔍 원인
- `StickyMemoWindow`는 `WS_EX_NOACTIVATE`로 동작하지만, `MainWindow`의 `임시메모` 버튼이나 `Xenie` 창을 거치면서 `_targetHwnd`가 `IntPtr.Zero`이거나 ChatHelper 자체 창 핸들로 남아 유효한 외부 채팅창을 잃어버리는 경우가 있음.
- 또한 `FastPasteText(memoText, dst)` 호출 시 `dst`가 `IntPtr.Zero`이면 포커스 복원(`ForceForegroundWindow`) 없이 `Ctrl+V`가 허공으로 전송됨.

### ✅ 구현 계획
1. **`OpenStickyMemo` / `ToggleWindow` 호출 시 유효 대상 창 폴백 강화**:
   - `Win32Api.IsValidTargetWindow(hwnd)`가 거짓이면 항상 `_hookService.LastTargetHwnd`를 전달.
2. **카드 `입력` 버튼 클릭 시 대상 창 검증 및 안내**:
   - `dst == IntPtr.Zero`이거나 `!Win32Api.IsValidTargetWindow(dst)`이면 `Win32Api.GetForegroundWindow()` → `_hookService.LastTargetHwnd` 순으로 재탐색.
   - 그래도 대상 채팅창이 없으면 입력할 채팅창을 먼저 클릭해 달라는 상태 배지(`"⚠️ 입력할 채팅창을 먼저 클릭해 주세요"`) 표시.
3. **`FastPasteText` 실행 전 안정화**:
   - `ForceForegroundWindow(dst)` 후 포커스 전환 완료(`Thread.Sleep(110)`)를 보장한 뒤 `Ctrl+V` 주입.

---

## 3. 변수 칸 `📋` 클립보드 채우기 시 텍스트 어색하게 이어붙음 + OCR `&` 오인식 보정

### 🔍 현상 (스크린샷 확인)
1. `Xenie`의 `송장번호:` 옆 `📋` 버튼으로 클립보드를 채울 때 여러 줄 텍스트나 UI 잔여 텍스트(`송장\n입력\n456-8280-8146\n송장\n입력...`)가 한 줄 `TextBox`에 그대로 들어가 줄바꿈이 깨지고 이어 붙음.
2. OCR 결과 중 `456-8280-8150`이 `456-828&8150`처럼 `0-`가 `&`로 오인식되는 케이스 발견.

### ✅ 구현 계획
1. **변수 칸(`TextBox`) 단일 값 정제 로직 추가** ([ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs)):
   - `📋` 클릭 시 클립보드에 여러 줄이 들어 있으면:
     - 해당 변수(`송장번호`, `날짜`, `금액`)의 **의미 패턴과 일치하는 첫 번째 유효 줄**을 우선 추출하고,
     - 없으면 비어 있지 않은 첫 번째 줄을 사용하며, `\r`/`\n`을 공백으로 정규화해 단일 줄 `TextBox`가 깨지지 않도록 처리.
   - `송장`, `입력`, `삭제` 같은 UI 버튼 텍스트가 섞인 경우 필터링.
2. **OCR 숫자 패턴 후처리 보강** ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs)):
   - 숫자 사이에서 `0-`가 `&`로 뭉개지는 패턴(`(?<=\d{3})&(?=\d{4})` 등, 예: `456-828&8150` → `456-8280-8150`)에 대한 후처리 보정 규칙 추가.

---

## 4. `Xenie` 최종 완성 문장 미리보기: 실행취소(되돌리기) · 비우기 버튼 추가

### ✅ 구현 계획
- [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml)의 `✨ 최종 완성 문장 미리보기` 상단 바(우측 `연결 형식` 옆)에 2개 버튼 추가:
  1. **`↩ 되돌리기 (원래 조합 복원)`**: 사용자가 `TxtFinalPreview`를 직접 수정하다가 망쳤을 때, 현재 메모 조각들과 변수 값으로 자동 조합된 원본 문장으로 즉시 복원(또는 직전 편집 스택 Undo).
  2. **`🧹 미리보기 비우기`**: `TxtFinalPreview` 내용만 깔끔하게 비우기(또는 메모 전체 초기화와 구분해 안내).

---

## 5. 메모 및 조합기 단축키 안내 강화

### ✅ 구현 계획
- 사용자가 단축키를 잊지 않도록 **핵심 단축키 안내를 UI에 상시·직관적으로 노출**:
  - **`챗헬프 임시메모` 하단 바**: `Alt+N 열기/닫기 · \+M 단어담기 · \+V 클립보드/OCR · \+C 비우기`
  - **`Xenie` 하단 바**: `\+N 열기 · \+M 담기 · \+F / Ctrl+Shift+Enter 최종입력 · Enter 좁히기`
  - **마스터창 툴팁 & 버튼 라벨**: `임시메모 (Alt+N)`, `Xenie (\+N)` 툴팁에 전체 단축키 요약 표기.

---

## 6. 기본 변수(오늘 날짜 등) 프리셋 제공

### ✅ 구현 계획
- `Xenie`의 **감지된 변수 입력 바** 및 `챗헬프 임시메모` 상단에 **자주 쓰는 기본 변수 원클릭 프리셋 칩** 제공:
  - **`📅 오늘 날짜`**: 예) `11월 27일 (금)` 또는 `2026-11-27` (클릭 시 `{날짜}` 변수 칸에 즉시 입력 또는 임시메모로 추가)
  - **`📅 내일 날짜`**: 예) `11월 28일 (토)`
  - **`🕒 금일 오후` / `🕒 내일 오전`**
  - 별도 타이핑 없이 1클릭으로 `{날짜}`/`{일정}` 빈칸을 채울 수 있도록 구성.

---

## 📌 반영 우선순위 (다음 작업 시 순서)
1. **버그 수정**: ① 임시메모 `[입력]` 대상 창 복구 → ② 자석 도킹 분리 쿨다운 → ③ 변수 `📋` 다중줄 정제 & OCR `&` 보정
2. **UX 개선**: ④ 미리보기 `↩ 되돌리기` / `🧹 비우기` → ⑤ 단축키 안내 보강 → ⑥ 오늘/내일 날짜 기본 변수 프리셋
3. **검증**: 단위 테스트 추가(`TemplateManagerTests`), `dotnet run --no-restore --project ChatHelper.Tests\ChatHelper.Tests.csproj` 통과 및 `dotnet publish` 재빌드, `PATCH_NOTES.md` v3.3.0 갱신.
