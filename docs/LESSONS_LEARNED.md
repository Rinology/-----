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

## 8. 물리 픽셀 좌표와 WPF DIP / 다중 모니터 좌표 혼용 금지
* **❌ 실패한 방법**: Win32 API(`ClientToScreen`, `accLocation`, `GetCursorPos`, `GetWindowRect`)가 반환하는 **물리 픽셀(Physical Pixels)** 좌표를 WPF `SystemParameters.PrimaryScreenWidth/Height`(주 모니터 DIP)와 직접 비교·클램핑하는 방식.
  * **실패 원인**: Windows 배율 125%·150% 노트북이나 듀얼 모니터 환경에서 물리 좌표가 논리 해상도보다 크거나 음수 좌표(왼쪽 모니터)가 되어, 알약(`SuggestionBar`)과 검색창(`QuickSearchWindow`)이 화면 밖으로 밀려나거나 주 모니터로 강제 이동해 안 보이는 현상이 발생함.
  * **✅ 검증된 해결책**: [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs)의 `ConvertPhysicalPointAndGetWorkArea`로 `TransformFromDevice` 변환 및 `MonitorFromPoint`/`GetMonitorInfo` 기반 해당 모니터 작업 영역을 구해 배치할 것.
* **❌ 실패한 방법 2**: 백그라운드 MTA 워커 스레드에서 매 키 입력마다 `AccessibleObjectFromWindow` 등 크로스-프로세스 COM이 포함된 `TryGetValidInputPosition`을 호출하는 방식.
  * **✅ 검증된 해결책**: 백그라운드 키 워커에서는 순수 Win32 `IsValidTargetWindow`로 대상 창 여부만 즉시 판별하고, 좌표 계산(`TryGetValidInputPosition`)은 UI STA 스레드에서 알약을 표시할 때만 실행할 것.

## 9. 제어판(커서 위치 등) 설정 변경 또는 자체 창 입력 시 `Alt+Q` 먹통 / `_lastTargetHwnd` 오염 금지
* **❌ 실패한 방법 1**: `KeyboardHookService.ProcessBufferedKeyDown`에서 대상 창이 ChatHelper 자신(`CurrentProcessId`)인지 먼저 검사하지 않고 무조건 `_lastTargetHwnd = currentHwnd`로 덮어쓰거나, `WinEventCallback`에서 `!_hasCandidates`일 때 `EVENT_SYSTEM_FOREGROUND` 외부 창 추적을 건너뛰는 방식.
  * **실패 원인**: 사용자가 제어판(`ControlPanelWindow`)에서 커서/알약 위치를 수정하거나 `Alt+Q`(`QuickSearchWindow`) 내부에서 키를 누르는 순간 `_lastTargetHwnd`가 ChatHelper 자신의 창 핸들로 덮어써짐. 이후 제어판을 닫고 `Alt+Q`를 누르면 대상 채팅창 핸들을 잃어버려 위치 계산(`TryGetValidInputPosition`)이 실패하고 항목 선택(Enter) 시 외부 채팅창에 텍스트가 입력되지 않음.
  * **✅ 검증된 해결책**:
    1. `HookCallback`에서 `Alt+Q`/`Alt+X`/`Alt+T` 전역 단축키 판별 직후 `!Win32Api.IsValidTargetWindow(currentFgHwnd)`이면 즉시 `CallNextHookEx`로 통과시켜 ChatHelper 자체 창 입력이 키 채널·완성 트리거·`_lastTargetHwnd`를 절대 건드리지 못하게 차단할 것.
    2. `WinEventCallback`에서 `_hasCandidates` 여부와 무관하게 `EVENT_SYSTEM_FOREGROUND` 발생 시 `Win32Api.IsValidTargetWindow(hwnd)`이면 항상 `_lastTargetHwnd`를 최신 외부 채팅창으로 갱신할 것.
    3. 제어판 저장 후 닫을 때 직전 유효 외부 채팅창(`_hookService.LastTargetHwnd`)으로 포커스를 복원하고, `QuickSearchWindow.ShowAtCursor`에서도 제어판의 `PillPlacement`(`CursorNearby` / `MousePointer` / `ScreenBottomRight`) 및 `PillOffsetY`, `_targetHwnd` 폴백을 동일하게 적용할 것.
* **❌ 실패한 방법 2**: `Alt+Q`, `Alt+X` 훅 가로채기 시 `VK_NONAME(0xFC)` 더미 키 전송을 누락하거나, `QuickSearchWindow`를 열 때 `_hookService.ClearAll()`을 호출하지 않고 `_suggestionBar.Hide()`만 호출하는 방식.
  * **실패 원인**:
    1. `Q`만 삼키고 `0xFC`를 보내지 않으면 대상 창(메모장·크롬 등)이 `Alt` 단독 탭으로 오인해 상단 메뉴바(`SC_KEYMENU`)를 활성화하여 `QuickSearchWindow`의 포커스를 빼앗음.
    2. `_suggestionBar.Hide()`만 호출하면 `_hookService._hasCandidates`가 `true`로 남아 `QuickSearchWindow` 내부에서 누른 `Tab`, `1`~`9`, `\` 키를 훅이 가로채 먹통이 됨.
    3. `QuickSearchWindow.Window_PreviewKeyDown`에서 한글 IME 조합 중(`Key.ImeProcessed`)일 때 `e.ImeProcessedKey`를 풀어주지 않으면 한글 검색어 입력 직후 `Down`/`Up`/`Enter`가 동작하지 않음.
  * **✅ 검증된 해결책**: `Alt+Q`·`Alt+X`·`Alt+T` 모두 `VK_NONAME(0xFC)`를 전송하고, 팝업 창 오픈 시 `_hookService.ClearAll()`을 반드시 호출하며, `PreviewKeyDown`에서 `Key.ImeProcessed`와 `Key.System`을 모두 언래핑(`actualKey`)하여 처리할 것.

## 10. 알약 자동 숨김(`0s`) 누락 및 화면 밖 알약 잘림 방지
* **❌ 실패한 방법**: `SuggestionBar`의 `_countdownTimer`가 `0s`에 도달했을 때 타이머만 멈추고 `Hide()`를 호출하지 않거나, `KeyboardHookService.OnAutoHideTimerElapsed`에서 `_currentCandidates.Count > 0`일 때만 `ClearCandidates()`를 호출하는 방식.
  * **실패 원인**: `[미일치]` 안내 알약처럼 후보 리스트가 비어 있는 상태에서 뜬 알약은 `_autoHideTimer` 조건을 통과하지 못하고 `_countdownTimer`도 `Hide()`를 부르지 않아 20초가 넘어도 `⏱ 0s` 상태로 화면에 계속 남아 있음.
  * **✅ 검증된 해결책**:
    1. [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs)의 `_countdownTimer.Tick`에서 `_remainingSeconds <= 0`일 때(선택 번역 카드 예외) 즉시 `Hide()`와 `AutoHideExpired` 이벤트를 호출해 `_hookService.ClearAll()`과 동기화할 것.
    2. [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)의 `OnAutoHideTimerElapsed`에서도 `_currentCandidates.Count > 0 || _buffer.Length > 0`을 모두 정리할 것.
    3. `SuggestionBar.ComputeAdjustedPosition`에서 입력 커서가 작업 영역 밖이면 마우스 커서로 폴백하고, 알약 크기로 화면 경계에 닿아 보정된 경우 `TxtPositionNotice`로 일시 조정 안내를 표시할 것.

## 11. `AllowsTransparency="False"` 환경의 진짜 반투명(`LWA_ALPHA` + `WM_STYLECHANGING` 훅) & 동기화 UI 반영
* **❌ 실패한 방법 1**: `AllowsTransparency="False"`인 WPF 창에 `this.Opacity = 0.7`만 설정하는 방식.
  * **실패 원인**: 비레이어드 WPF 창의 `Opacity`는 뒤의 다른 프로그램 창을 비추는 대신 **자체 검은 백버퍼와 혼합되어 화면만 어둡게 만듦**.
* **❌ 실패한 방법 2**: `AllowsTransparency="False"`인 WPF 창에 단순히 `SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED)` + `SetLayeredWindowAttributes`만 호출하는 방식.
  * **실패 원인**: `SetWindowLong(GWL_EXSTYLE)` 호출 시 OS가 동기적으로 `WM_STYLECHANGING(0x007C)` 메시지를 WPF `Window.WndProc`(`Window.WmStyleChanging`)으로 보내는데, WPF 내부 코드가 `AllowsTransparency == false`이면 `STYLESTRUCT.styleNew`에서 `WS_EX_LAYERED` 비트를 강제로 다시 제거해 버림 (`ExStyle` 확인 시 `HasLayered=False`가 되어 `SetLayeredWindowAttributes`가 무시됨).
  * **✅ 검증된 해결책**: `AllowsTransparency="True"`는 마우스 스터터 방지를 위해 계속 끄고, 대신 [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs)의 `SetWindowAlpha`에서 **`HwndSource.FromHwnd(hwnd).AddHook`으로 `WM_STYLECHANGING(0x007C)`을 가로채 `STYLESTRUCT.styleNew`(`lParam + 4`)에 `WS_EX_LAYERED(0x00080000)`를 강제 보존(`handled = true`)**한 뒤 `SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA)`를 호출할 것. (`GetLayeredWindowAttributes` 단위 테스트로 `HasLayered=True, Alpha=115/255` 검증 완료)
* **❌ 동기화 누락**: `ControlPanelWindow`가 생성자에서만 `LoadData()`를 호출해 백그라운드 동기화 완료 후에도 이전 시트 목록이 남아 있던 문제.
  * **✅ 검증된 해결책**: `TemplateManager.TemplatesSynced` 이벤트를 구독(`Closed` 시 해제)하여 동기화 완료 즉시 목록·프로필을 갱신할 것.

## 12. 전역 핫키(`Alt+Q`) 무응답 · 창 뒤로 밀림 방지 및 진단 로그 기록
* **❌ 실패한 방법**:
  1. `RegisterHotKey(Alt+Q/X)` 반환값(`false`)과 `GetLastWin32Error()`를 확인·기록하지 않고 무시하거나, `OpenQuickSearch` 예외를 기록하지 않는 방식.
  2. 숨겨진 WPF 팝업(`QuickSearchWindow`)을 다시 열 때 `Show()`만 호출하고 `ShowActivated = true`, `Topmost = true`를 명시하지 않는 방식.
* **실패 원인**:
  * 타 프로그램이 `Alt+Q`를 선점했거나, **관리자 권한으로 실행된 앱 위에서 `WH_KEYBOARD_LL` 키훅이 Windows UIPI 보안 정책에 의해 차단**될 경우 `RegisterHotKey` 실패 여부를 로그로 남기지 않으면 원인 파악이 불가능함.
  * 숨김(`Hide()`) 후 재오픈 시 `ShowActivated`·`Topmost`가 명시되지 않으면 외부 창 뒤에 가려지거나 포커스를 받지 못해 동작하지 않는 것처럼 보임.
* **✅ 검증된 해결책 및 결과**:
  * [QuickSearchWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml.cs) `ShowAtCursor`에서 `ShowActivated = true`, `Topmost = true`를 명시적으로 강제하여 항상 최상단·활성 상태로 열리도록 보강.
  * [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs)의 `RegisterGlobalHotkeys` 및 `OpenQuickSearch`에서 `RegisterHotKey` 실패 코드(`GetLastWin32Error`)와 창 열기 예외를 `3/3 로그` 패널과 `ChatHelper.log`에 즉시 기록해 환경별 장애 원인을 바로 추적할 수 있도록 개선.

## 13. `WH_KEYBOARD_LL` 훅 내부 `_lock` 경합(간헐적 채팅 지연) 및 `OnSourceInitialized` 핫키 등록 누락 금지
* **❌ 실패한 방법 1**: `KeyboardHookService.HookCallback`에서 `Space`, `A`, `E`, `1`~`9` 키가 눌릴 때마다 후보 표시 여부(`_hasCandidates`)나 트리거 대기 상태(`_triggerArmed`)와 무관하게 `_keyChannel`을 동기 플러시(`ProcessBufferedKeyDown` → `lock (_lock)` → `EvaluateCandidates()`)하는 방식.
  * **실패 원인**: 일반 채팅 중 `Space`, `ㅁ`(`A`), `ㄷ`(`E`), 숫자를 빠르게 입력할 때마다 OS 저수준 키훅(`WH_KEYBOARD_LL`) 스레드가 백그라운드 워커의 `_lock` 해제를 기다리며 블로킹되거나 퍼지 검색을 동기 실행해 **간헐적 채팅 타이핑 지연**이 발생하고, `LowLevelHooksTimeout` 초과 시 OS가 키훅을 조용히 해제해 `Alt+Q`까지 먹통이 될 수 있음.
  * **✅ 검증된 해결책**: [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)에서 `ShouldHandleTriggerInHook`으로 실제 완성 트리거(`\`, 후보 표시 중 `Tab` / `Shift+Space` / `\` 이후 `1~9`·`A`·`E`)일 때만 채널 플러시·`_lock` 진입을 허용하고, `IsValidTargetWindowCached`로 매 키 입력마다 `StringBuilder` 할당·`GetClassName` 호출이 발생하지 않도록 캐싱할 것.
* **❌ 실패한 방법 2**: `MainWindow.OnSourceInitialized`에서 `PresentationSource.FromVisual(this) as HwndSource`만 단일 체크하고 `null`일 때 폴백 없이 넘어가는 방식.
  * **실패 원인**: `ChatHelper.log` 진단 결과 `훅 설치 완료`는 기록되었으나 `RegisterGlobalHotkeys` 로그가 전혀 남지 않는 케이스가 발생했으며, `_hwndSource`가 `null`로 평가되면 `HwndHook`과 `RegisterHotKey(Alt+Q/X)`가 모두 누락되어 `WM_HOTKEY`가 동작하지 않음.
  * **✅ 검증된 해결책**: [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs)의 `EnsureGlobalHotkeyHookRegistered()`에서 `new WindowInteropHelper(this).Handle` → `HwndSource.FromHwnd`를 우선 사용하고, `Window_Loaded`에서도 미등록 시 재시도하도록 이중 보장할 것.
