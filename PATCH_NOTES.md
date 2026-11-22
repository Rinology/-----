# 📋 ChatHelper 패치노트 (Patch Notes)

> 모든 기능 추가, 버그 수정, UI/UX 개선 및 `publish` 배포 이력을 최신순으로 기록합니다.

---

## [v2.4.0] — 2026-11-22 (현대적 UI/UX 전면 개편 — Fluent 라이트 & 미니멀 HUD 도킹 바)

### ✨ Added / Changed
- **유저 개인 시트 동적 추가·삭제 지원 (GAS `프로필목록` 탭 로밍 + 로컬 PC 전용 시트) — ⚠️ GAS 재배포 1회 필요**:
  - [GAS/Code.gs](file:///C:/Users/taerin/Desktop/채팅도우미/GAS/Code.gs)에 `프로필목록` 탭 동적 로드(`loadProfiles_`) 및 `add_profile` / `delete_profile` 액션 추가 (`owner_id = ALL`은 공용 시트, `owner_id = 본인아이디`는 본인만 조회·추가·삭제 가능, 타인 시트·공용 시트 삭제 차단).
  - [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml) > **`🗂 데이터 소스`** 탭에서 **`☁️ GAS 클라우드(내 계정 로밍 · 비공개 시트 읽기/쓰기)`** 또는 **`💻 이 PC 로컬 전용(읽기 전용)`** 방식을 선택해 개인 시트를 직접 추가/삭제할 수 있습니다.
  - 이번 `GAS/Code.gs`를 1회 새 버전으로 배포하면, 이후에는 공용·개인 시트 추가/삭제 시 GAS 코드를 다시 수정·배포할 필요가 없습니다.
- **시트 바로등록 기능 임시 비활성화 & `Alt + T` 번역 메뉴/치환 수정**:
  - `+ 등록`, `+ 시트 등록`, `Alt + R`, 제어판 즉시 저장 옵션을 잠시 숨김/주석 처리.
  - `Alt + T` 입력 시 대상 창의 Alt 기본 메뉴바가 열리면서 포커스를 빼앗아 `[선택 영역을 영문으로 바꾸기]`·`[원문+영문 함께 입력]`이 동작하지 않던 문제를 `VK_NONAME(0xFC)` 처리 및 `Alt` 키 해제로 해결.
  - [AuthService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/AuthService.cs)의 마지막 로그인 일시(`last_login`) 기록 결과를 `MainWindow`가 놓치지 않고 대기·로그 출력하도록 보강 (`GAS/Code.gs` 새 버전 재배포 필요 여부 확인 가능).
- **드래그 선택 기반 Google 영문 번역(`Alt + T`) & 로딩 UI 도입**:
  - 기존 타이핑 중 문장 자동 추측 번역(어디부터 어디까지 번역되는지 모호하던 문제)을 제거하고, **번역할 문장을 마우스/키보드로 드래그(선택)한 뒤 `Alt + T`를 누르면** 선택한 원문 미리보기 + **로딩 화면(`🌐 Google 영문 번역 중...` + 프로그레스 바)**이 즉시 표시되도록 개선.
  - 번역 완료 후 Google 번역기형 2열 비교 카드에서 **`선택 영역을 영문으로 바꾸기`** 또는 **`원문+영문 함께 입력`**을 누르면 드래그했던 범위만 정확히 치환됨.
- **초경량 시트 빠른 등록 창([QuickRegisterDialog.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickRegisterDialog.cs)) & `Alt + R` 단축키**:
  - `+ 등록` / `+ 시트 등록` 클릭 시 무거운 `QuickSearchWindow`를 생성하지 않고 `<10ms` 만에 즉시 열리는 전용 등록 창으로 교체.
  - 본문 범위를 명확히 확인할 수 있고, 저장 버튼 클릭 시 **업로드 진행률 바(`⏳ 시트에 저장 중...`)**가 표시되며, 채팅창에서 문장을 드래그한 뒤 **`Alt + R`**을 누르면 해당 문장이 본문에 바로 채워진 상태로 등록 창이 열림.
- **구글 번역기형 실시간 번역 패널 (`SuggestionBar`)**:
  - 제어판에서 번역 옵션을 켰을 때 문장 입력 시 **좌측 `한국어 원문` ↔ 우측 `Google 영문 번역` 2열 비교 카드**와 `[\+Tab] 영문만 입력` / `원문+영문 함께 입력` / `복사` 버튼 제공.
- **제어판 `커맨드 관리` 검색 필터 & 구글 시트 양방향 CRUD**:
  - 상단 검색 필터(`TxtCmdFilter`) 및 더블클릭 수정, `신규 추가` / `수정 적용` / `선택 삭제` 시 구글 시트(`GAS update/delete/append`)에도 즉시 반영.
- **마스터창(`MainWindow`) 로그 패널 상향 자동 확장**:
  - 우측 하단 배치 상태에서 `로그`를 열 때 화면 아래로 잘리지 않도록 자동으로 위로 펼쳐지고, 닫으면 원래 위치로 복귀.
- **단어(키워드 매칭) vs 문장(구글 영문 번역) 완벽 분리**:
  - 공백 없는 1단어 입력 시에는 오직 스프레드시트 키워드 매칭만 수행하고, 띄어쓰기 2어절 이상 문장 입력 시에는 시트 검색을 생략하고 오직 Google 번역(GAS)만 실행.
- **GAS `last_login` 기록 안정화 (`doGet` 지원 + 실시간 로그 출력)**:
  - [GAS/Code.gs](file:///C:/Users/taerin/Desktop/채팅도우미/GAS/Code.gs)에서 `update_last_login`을 `doGet`으로도 처리하고 헤더 인식(`로그인`/`접속`/`last_login`)을 확장했으며, 결과를 도킹 바 `로그` 패널에 출력하도록 개선.
- **GAS 내장 Google 번역(`LanguageApp.translate`) 실시간 영문 번역 연동**:
  - [GAS/Code.gs](file:///C:/Users/taerin/Desktop/채팅도우미/GAS/Code.gs)에 `action=translate` 엔드포인트를 추가해 Ollama 없이도 무료 Google 번역 품질로 한글 → 영문 실시간 번역 지원 (`\+E` 또는 시트 후보 없을 때 `\+Tab`으로 치환, 제어판 ON/OFF 토글).
- **비공개 사용자 시트 GAS 통합 & 마지막 로그인 일시(`last_login`) 자동 기록**:
  - [GAS/Code.gs](file:///C:/Users/taerin/Desktop/채팅도우미/GAS/Code.gs) v5에 `action=users`(토큰 인증 읽기)와 `action=update_last_login`(로그인 시 한국시간 `yyyy-MM-dd HH:mm:ss` 자동 기록) 추가.
  - 사용자 시트를 외부에 '웹에 게시'하거나 '링크 공유'로 열지 않고 100% 비공개 상태로 인증 및 접속 기록 관리 가능.
- **계정별 업무 프로필 접근 권한 제어 (`AllowedProfiles`)**:
  - 사용자 인증 시트(USER_SHEET)에 `allowed_profiles` (또는 `프로필` / `권한` / `부서`) 열을 추가하면 계정별로 접근 가능한 프로필만 표시 (`물류`, `CS상담`, `물류,CS상담`, 비어 있거나 `ALL`이면 전체 허용).
  - 권한이 없는 프로필이 기존 설정에 남아 있어도 로그인 시 허용된 첫 번째 프로필로 자동 전환.
- **현대적 디자인 시스템 도입 (`FluentLight` 기본 / `MinimalHud` 선택 옵션)**:
  - 기존의 구식 Windows 98 4겹 베벨 스타일을 현대적인 **Windows 11 Fluent 라이트**(`#F8FAFC` / `#FFFFFF` / `#2563EB` / 라운드 카드)로 전면 개편.
  - **제어판 > 디자인 & 표시**에서 **`Windows 11 Fluent 라이트`**와 반투명 슬레이트 기반 **`미니멀 글래스 HUD`** 테마를 실시간 미리보기로 전환 가능.
- **마스터창(`MainWindow`) 초슬림 1줄 플로팅 HUD 도킹 바로 개편**:
  - 화면을 거의 가리지 않는 가로 1줄 도킹 바(`ON/OFF 토글 · 업무 프로필 · 검색 · 기호 · + 등록 · 동기화 · 로그 · 설정`)로 단순화하고, 과한 이모지를 제거해 깔끔한 프로 도구 감성으로 정리.
  - `로그` 클릭 시 도킹 바 아래로 실시간 상태 로그 패널이 펼쳐짐.
- **보조 창 UI 통일**:
  - 로그인 창(`LoginWindow`), 종료/로그아웃 확인 모달(`ExitConfirmWindow`), 추천 알약(`SuggestionBar`), 빠른 검색(`QuickSearchWindow`), 특수기호(`SymbolPickerWindow`), 제어판(`ControlPanelWindow`) 모두 모던 라운드 카드 & 부드러운 그림자 스타일로 통일하고 로그인 안내 문구(`유저 정보를 확인하고 있습니다...`)를 자연스럽게 개선.

### 🐛 Fixed
- **수동 동기화 시 타 사용자 목록 노출 차단 & 데이터 소스 UI/안내 개선**:
  - [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs#L340-L355): `지금 즉시 최신 정보 동기화 실행` 시 타 직원 계정 목록(`SyncUserListFromRemoteAsync`)을 내려받거나 표시하지 않도록 제거(현재 프로필·템플릿만 동기화).
  - [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml#L383-L481) / [ControlPanelWindow.DataSources.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.DataSources.cs):
    - **`☁️ 서버 시트 (계정 로밍)`**와 **`💻 로컬 PC 시트 (이 PC 전용)`**를 별도 서브 탭으로 분리하고, 각각 전용 상태/로딩 표시를 두어 상단 업무 프로필 영역과 섞이지 않도록 개선.
    - 필수 1행 헤더(`keyword | contentKeyword | title | content | category`) 안내 및 `taerin@qualisports.kr` 편집자 추가 안내(+ 원클릭 복사 버튼) 추가.
- **모든 PC 환경 DWM 마우스 스터터 근본 차단 (`AllowsTransparency` 제거 + `SoftwareOnly` 렌더링)**:
  - 전 창(`MainWindow`, `SuggestionBar`, `QuickSearchWindow`, `SymbolPickerWindow`, `ControlPanelWindow`, `ExitConfirmWindow`, `LoginWindow`)에서 `AllowsTransparency="True"`(`WS_EX_LAYERED`)와 반투명 배경을 제거하고 불투명 네이티브 창으로 전환.
  - [App.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/App.xaml.cs)에 `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` 및 Windows 11 네이티브 라운드 코너(`DwmSetWindowAttribute`)를 적용해 고주사율·듀얼 모니터·하이브리드 GPU·원격 환경에서도 마우스/UI 지연이 발생하지 않도록 개선.
- **키보드 훅(`WH_KEYBOARD_LL`) 동기 플러시 경량화 & WPF 그림자 효과 경량화**:
  - [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)에서 트리거 키(`\`, `Tab`, `Space` 등) 입력 시 `WH_KEYBOARD_LL` 콜백 내부에서 크로스-프로세스 COM 위치 조회(`TryGetValidInputPosition`)를 실행하지 않도록 분리(`skipPositionCheck: true`)해 고속 타이핑 시 훅 지연을 제거.
  - [MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [SuggestionBar.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml) 등 7개 반투명 창의 `<DropShadowEffect>` 소프트웨어 블러를 제거해 저사양·신규 PC 및 원격 데스크톱 환경의 창 렌더링 부하를 경량화.
- **전역 마우스 훅(`WH_MOUSE_LL`) 제거로 마우스 커서 심각한 지연(렉) 해결 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs))**:
  - 기존 `WH_MOUSE_LL` 전역 마우스 훅이 모든 `WM_MOUSEMOVE` 이벤트를 WPF UI 스레드로 동기 전달해 고주사율 마우스·신규 PC 환경에서 커서 움직임이 극심하게 느려지던 문제를 해결.
  - 전역 마우스 훅을 완전히 제거하고 비동기 out-of-context `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_FOCUS` + `WINEVENT_SKIPOWNPROCESS`)으로 대체하여 마우스 이동 지연 `0ms`로 개선.
- **보안 감사 및 추가 하드닝 완료**:
  - `TemplateManager.cs` 및 `AppLogger.cs`에서 동기화 실패 로그에 GAS URL·`token`·시트 URL이 남지 않도록 `SanitizeSecrets` 마스킹 적용.
  - 15분 주기 동기화의 잔여 `GlossaryService` 호출 및 디스크 CSV 저장 제거, 로그인 완료 즉시 `AuthService.ClearUserDirectory()`로 메모리 내 타 직원 계정 목록 제거.
- **비밀번호 디스크 저장 완전 제거 (`config.json`) & 로그 가독성 개선**:
  - `config.json`에서 `SavedPassword` 저장을 완전히 제거해 **아이디만 기억**하고 매번 비밀번호를 직접 입력하도록 보안 강화.
  - `AppLogger`가 UTF-8 BOM으로 `ChatHelper.log`를 기록해 메모장에서 한글 깨짐을 방지하고, 긴 HTML 응답은 `[HTML 오류: ...]`로 자동 요약.
- **로컬 평문 `users.csv` 및 `chat_templates*.csv` 디스크 백업 제거 (보안 강화)**:
  - 전 직원 아이디·비밀번호(`users.csv`)와 시트 템플릿(`chat_templates*.csv`)을 실행 폴더에 파일로 남기지 않고, 항상 구글 스프레드시트(GAS)에서 메모리로만 직접 조회하도록 변경.
- **GAS HTML/오류 응답 시 로컬 캐시 보호 (`TemplateManager.cs`)**:
  - GAS 권한 설정 오류 등으로 CSV 대신 HTML(`<!DOCTYPE html>`)이나 `Server error:`가 반환될 때 0개로 덮어쓰지 않고 기존 로컬 캐시를 유지하도록 방어 로직 추가.
- **제안 알약(`SuggestionBar`) 위치 일관성 개선**:
  - `DesiredSize` 사전 측정 + 화면 하단 채팅 영역(`65%` 이하)에서는 `[미일치]`와 `[매칭]` 모두 항상 커서 위쪽에 일관되게 배치되도록 수정해 위치가 위아래로 튀던 현상 해결.
- **실시간 영문 번역 로딩 안내 및 단독 치환 수정**:
  - 시트 후보가 없을 때도 `"EN 번역 중..."` 상태가 표시되고, 번역 완료 후 `\+Tab` / 클릭으로 즉시 치환되도록 `_matchedLen`/`_targetHwnd` 전달 보강.

---

## [v2.3.0] — 2026-11-22 (초경량 키워드·특수기호 전환 & GAS v4 보안/프로필 통합)

### ✨ Added (신규 기능)
- **알약 자동 숨김 남은 시간 카운트다운 (`⏱ Ns`)**:
  - 추천 알약 상단 헤더에 닫히기까지 남은 초(`⏱ 12s` → `11s` ...)를 1초 단위로 실시간 표시.
- **시트 등록 즉시 저장 토글 (`QuickSaveWithoutDialog`)**:
  - 제어판(`🔄 동기화 & 계정`)에서 상세 입력창을 띄울지, 묻지 않고 클립보드 내용·단축어를 바로 시트에 저장할지 선택 가능.
- **마스터 화면 실시간 상태 로그 패널 (`📋 로그`)**:
  - 메인 상태창에 `📋 로그` 토글 버튼을 추가하여, 시트 불러오기·동기화·시트 저장 진행/완료/실패 로그를 실시간으로 확인 가능.
- **`Alt+X` 특수문자·이모티콘 전용 창 (`SymbolPickerWindow`)**:
  - `Alt` 조합 없이 **숫자키 `1`~`9` 단독 입력**(키패드 포함) 또는 **`←↑↓→` + `Enter`**로 즉시 특수기호를 입력해 GeForce Experience 등 외부 단축키와의 충돌을 원천 차단.
  - 사용자가 직접 특수문자·이모티콘을 **`➕ 추가` / `🗑 선택삭제` / `↩ 기본 복원`** 가능 (`config.json` 저장).
- **추천 알약(`SuggestionBar`) 내 `➕ 시트 등록` 원클릭 업로드**:
  - 타이핑 중 인식된 단어 배너(`🔎 인식 구간` / `❓ [알아듣지 못함]`) 우측에 **`➕ 시트 등록`** 버튼을 추가하여, 클릭 즉시 인식된 단어를 단축어·제목으로 자동 채운 시트 등록 창 호출.
- **클립보드 연동 `➕ 답변 시트 등록`**:
  - 메인 상태창 및 `Alt+Q`에서 새 답변 등록 시 클립보드(`Ctrl+C`)에 복사해 둔 문장을 본문 칸에 자동 입력.
- **업무 프로필 전환 (`물류` / `CS상담`) & GAS 중앙 관리**:
  - 메인 상태창 드롭다운 1클릭 및 제어판(`🗂 데이터 소스`)에서 업무별 구글 스프레드시트 즉시 전환 및 로딩 상태 표시.
  - 메인 상태창의 `🔄 시트 [...]` 텍스트 클릭 시 즉시 수동 동기화 실행.
  - 프로필별 로컬 캐시 파일 자동 분리 (`chat_templates_물류.csv`, `chat_templates_CS상담.csv`).
- **`Alt+Q` 최근 사용 템플릿 상단 고정 & `⭐ 최근 사용` 탭**:
  - 최근 사용한 템플릿 최대 10개를 기억하여 검색어가 없을 때 최상단에 우선 정렬.
- **알약 표시 위치 옵션 확장**:
  - 기본 **채팅(키보드) 텍스트 커서(Caret)** 추적(Win32 Caret + MSAA `OBJID_CARET` + 한글 IME `ImmGetCompositionWindow`) 외에 제어판에서 **마우스 포인터 위치 기준** 옵션 선택 가능.

### 🔒 Security (보안 강화)
- **민감 URL/토큰 AES-256 암호화 (`SecretVault`) & `.pdb` 제거**:
  - `DefaultGasWebhookUrl`, `DefaultGasApiToken`, 폴백 시트 URL을 AES-256으로 암호화해 바이너리 내 평문 노출을 차단.
  - Release 빌드 시 디버그 심볼(`.pdb`)을 생성하지 않도록 설정하고, 개인 캐시/로그가 제외된 깨끗한 단일 파일 배포 폴더(`dist/`) 구성.
- **GAS v4 API 토큰 인증 (`API_TOKEN`)**:
  - 모든 `doGet`(`profiles`, `read`) 및 `doPost`(등록/삭제) 요청에 사내 전용 보안 토큰 검증을 추가해 외부 무단 접근 차단.
  - 시트를 외부에 '웹에 게시'하거나 '링크 공유'로 열지 않아도 GAS가 읽기·쓰기를 모두 안전하게 중계.

### 🐛 Fixed (버그 수정)
- **한글 IME 백스페이스 동기화 (`HangulImeBuffer`)**:
  - 조합 중인 자모 삭제(1자모)와 확정된 음절 삭제(1음절 전체)를 윈도우 한글 입력기와 동일하게 동기화하여 `"가로형 형"`처럼 이전 글자가 남던 현상 해결.
- **답변 시트 등록 창 리소스 오류 수정**:
  - 존재하지 않던 `ThemeTextBoxStyle` 리소스 참조로 인해 등록 창 호출 시 발생하던 예외 해결.

### 🗑️ Removed (경량화 및 제거)
- **Ollama 코드·AI 에이전트 프롬프트 완전 삭제**:
  - `OllamaService.cs`, `QuickAiWindow.xaml(.cs)`, `Ollama/Modelfile`, `ChatHelper/Prompts/`, `docs/AI_PROMPT_GUIDE.md` 및 관련 설정(`EnableLocalAi`, `AiPlacement`, `AutoGenerateOnAltX`, `AutoUploadAiSelection`)을 코드베이스에서 완전히 삭제.
- **로컬 AI(Ollama) / 용어집 / 톤앤매너 제거**:
  - 저사양 PC에서의 리소스 점유와 부정확한 생성 문제를 없애기 위해 Ollama 연동, `AI 답변 생성 중...` 알림, 용어집·톤앤매너 탭 동기화를 제거하고 **`채팅템플릿`(1번 시트) 단일 탭 초경량 구조**로 단순화.
  - 직원 PC 배포 시 `.env`나 Ollama 설치 없이 `publish` 폴더(exe) 단독으로 즉시 실행 가능.
