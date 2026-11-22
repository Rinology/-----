# 🚫 ChatHelper 실패 이력 및 금지 패턴 (Lessons Learned & Anti-Patterns)

> **에이전트 필독**: 코드를 수정하거나 새로운 기능을 구현하기 전에 반드시 이 문서를 확인하세요.
> 아래 기록된 **실패한 접근법(❌)**은 이미 시행착오를 거쳐 폐기된 방식이므로 절대 다시 사용하지 마세요.

---

## 1. 한글 입력 & 키보드 후킹
* **❌ 실패한 방법**: `StringBuilder`에 영문 QWERTY 키를 평면으로 쌓고 `VK_BACK` 시 `_buffer.Length--`로 1글자씩 지우는 방식.
  * **실패 원인**: 윈도우 한글 IME는 조합 중일 때는 자모 1개씩 지우지만, 확정된 음절(예: 스페이스 뒤)을 지울 때는 **1음절 전체(2~5타)**를 한 번에 삭제함. 이로 인해 `"가로형 "` → `Backspace` × 2 → `"형"` 입력 시 `"가로형 형"`처럼 잔상이 남음.
  * **✅ 검증된 해결책**: [HangulAssembler.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/HangulAssembler.cs)의 `HangulImeBuffer`처럼 조합 중(`_composingRaw`)과 확정 음절(`_committed`)을 분리해 상태 머신으로 관리할 것.

## 2. 텍스트 치환 & 키 주입 (`SendInput`)
* **❌ 실패한 방법**: `keybd_event` 또는 `KEYEVENTF_EXTENDEDKEY` / 하드웨어 스캔코드 없이 `VK_HOME`, `VK_RIGHT`를 보내는 방식.
  * **실패 원인**: NumLock이 켜진 환경에서 `Shift + Home` 등이 무시되어 기존 텍스트가 지워지지 않고 끝에 잔여 글자가 남음.
  * **✅ 검증된 해결책**: [TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs)의 `SendInput` + `KEYEVENTF_EXTENDEDKEY` + `MapVirtualKey` 스캔코드 방식 유지.

## 3. 커서 위치 추적 (알약 표시 위치)
* **❌ 실패한 방법**: `GetGUIThreadInfo`의 `hwndCaret`이 `IntPtr.Zero`인 모던 앱(Chrome, Edge, Electron)에서 무조건 `GetCursorPos()`(마우스 포인터)로 폴백하는 방식.
  * **실패 원인**: 사용자가 마우스를 다른 곳에 두면 채팅 입력창이 아닌 마우스 포인터를 따라다니며 알약이 뜸.
  * **✅ 검증된 해결책**: [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs)의 `TryGetValidInputPosition`에서 **Win32 Caret → MSAA `OBJID_CARET` → 한글 IME `ImmGetCompositionWindow` → 직전 커서/입력창 하단 고정** 순으로 탐색. 마우스 위치는 사용자가 제어판에서 `MousePointer` 옵션을 직접 켰을 때만 사용.

## 4. 단축키 & UI 리소스
* **❌ 실패한 방법 1**: `Alt+Q` 창 안에서 `Alt+1`~`9`로 특수기호를 입력하게 하는 방식.
  * **실패 원인**: GeForce Experience 등 외부 오버레이 단축키(`Alt+1~9`)가 먼저 가로채거나, 한글 IME 상태에서 `Key.ImeProcessed`로 전달됨.
  * **✅ 검증된 해결책**: 특수기호는 `Alt+X` 전용 창([SymbolPickerWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SymbolPickerWindow.xaml.cs))으로 분리하고, 창 내부에서는 `Alt` 없이 **단독 숫자키 `1`~`9`**(및 `ImeProcessedKey` 처리)로 입력.
* **❌ 실패한 방법 2**: 코드 비하인드에서 `(Style)FindResource("ThemeTextBoxStyle")`처럼 존재 여부가 불확실한 리소스를 강제 캐스팅하는 방식.
  * **실패 원인**: `InvalidCastException` / `ResourceReferenceKeyNotFoundException` 런타임 크래시 유발.
  * **✅ 검증된 해결책**: `TryFindResource("...") as Style` 사용.

## 5. 구글 시트(GAS) & 캐시 관리
* **❌ 실패한 방법**: 모든 프로필이 하나의 `chat_templates.csv` 캐시 파일을 공유하거나, GAS 웹앱을 토큰 없이 열어두는 방식.
  * **실패 원인**: 프로필(`물류` ↔ `CS상담`) 전환 직후 오프라인이 되면 이전 프로필 캐시가 노출됨. 또한 토큰 없는 GAS URL은 외부 유출 시 무단 읽기/쓰기 위험이 있음.
  * **✅ 검증된 해결책**: [DataSourceResolver.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/DataSourceResolver.cs)의 `GetScopedCachePath`로 프로필별 캐시 분리 및 `API_TOKEN` 인증 유지.

## 6. 로컬 AI(Ollama) & 빌드 환경
* **❌ 실패한 방법**: 8GB RAM 저사양 사내 PC에서 `qwen2.5:1.5b` / `3b` 로컬 LLM을 상시 예열·백그라운드 추론하는 방식.
  * **실패 원인**: 리소스 점유 대비 CS 답변 품질이 낮고 `"AI 답변 생성 중..."` 알림이 실무 흐름을 방해함.
  * **✅ 검증된 해결책**: 로컬 AI(Ollama) 제거, 정해진 키워드 템플릿 + 원클릭 시트 등록 구조 유지.
* **❌ GAS 웹앱 액세스 권한 오류 (`<!DOCTYPE html>` 반환)**: Google Workspace(`@회사도메인`) 계정으로 GAS 웹앱을 배포할 때 **`액세스 권한이 있는 사용자`**가 `모든 사용자(Anyone)`가 아니라 `회사 내 모든 사용자` 또는 `나만`으로 되어 있으면, 비로그인 `HttpClient` 요청에 구글 로그인 HTML(`<!DOCTYPE html>`)이 반환됨. Workspace 정책상 `모든 사용자`가 막혀 있다면 개인 `@gmail.com` 계정으로 GAS를 배포하고 시트들에 편집자로 초대할 것.
* **❌ 빌드 주의**: 샌드박스 환경에서 `dotnet restore` 실행 시 `NuGet.Config` 접근 권한 오류가 발생하므로, 빌드/테스트/배포 명령에는 반드시 **`--no-restore`** 옵션을 사용할 것.
* **❌ Git 대용량 파일 커밋 금지 (`dist/`, `100MB+`)**: `Self-Contained + SingleFile`로 빌드한 `dist/ChatHelper.exe`(약 178MB)를 Git에 커밋하면 GitHub 100MB 파일 크기 제한으로 Push가 거부됨. `dist/`는 `.gitignore`로 제외하고, 다른 PC에서 받아 재빌드할 때는 아래 명령어를 실행할 것:
  ```powershell
  dotnet restore ChatHelper\ChatHelper.csproj -r win-x64
  dotnet publish ChatHelper\ChatHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o dist
  ```

## 7. 전역 마우스 훅(`WH_MOUSE_LL`) 및 `AllowsTransparency` 금지
* **❌ 실패한 방법 1**: 외부 창 클릭 시 입력 버퍼를 비우기 위해 WPF UI 스레드에 `WH_MOUSE_LL` 전역 마우스 훅을 설치하는 방식.
  * **실패 원인**: `WM_LBUTTONDOWN`만 필요해도 OS가 모든 `WM_MOUSEMOVE`(125~1000Hz) 이벤트를 UI 스레드로 동기 전달하므로, UI 스레드가 잠깐이라도 바쁘거나 고주사율 마우스를 쓰는 PC에서 시스템 전체 마우스 커서가 극심하게 느려짐.
  * **✅ 검증된 해결책**: [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)처럼 `WH_MOUSE_LL`을 사용하지 말고, 비동기 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND / EVENT_OBJECT_FOCUS, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`로 창 전환·포커스 이동을 감지할 것.
* **❌ 실패한 방법 2**: `MainWindow`·`SuggestionBar` 등 상시/빈번 노출 창에 `AllowsTransparency="True"` + `DropShadowEffect` + 기본 D3D9 하드웨어 가속을 사용하는 방식.
  * **실패 원인**: 고주사율 모니터(120Hz+), 주사율 불일치 듀얼 모니터, 하이브리드 GPU(Optimus/Intel) 환경에서 `WS_EX_LAYERED` + D3D9 창이 Windows DWM/MPO와 충돌해 마우스 커서 스터터를 유발함.
  * **✅ 검증된 해결책**: `AllowsTransparency="True"` 및 `<DropShadowEffect>`를 제거하고, [App.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/App.xaml.cs)에서 `RenderMode.SoftwareOnly` + Windows 11 네이티브 `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE)`를 적용할 것.
