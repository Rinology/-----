# 📋 ChatHelper 패치노트 (Patch Notes)

> 모든 기능 추가, 버그 수정, UI/UX 개선 및 `publish` 배포 이력을 최신순으로 기록합니다.

---

## [v3.5.5] — 2026-11-28 (연속 변수 입력 UX & 임시메모 자동 소모 연동 · 문장/제목 검색 보강 · 알약 하단 강조 및 단축키 분리 · UI/메모 통합 테마)

### ✨ Added / Changed
- **답변조합기(`Xenie`) 연속 입력 시 창 유지 및 사용한 임시메모 자동 소모 연동 ([ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - 최종 완성 입력 후 창이 닫히지 않고 유지되도록 변경해, 여러 송장번호를 연속으로 안내할 수 있도록 개선.
  - **`사용한 임시메모 삭제`** 체크박스(기본 체크)와 **`입력 후 조합칸 비우기`** 체크박스(기본 해제)를 분리하여, 완성 입력 시 변수에 사용된 임시메모 카드가 자동 삭제되고 남은 다음 임시메모(송장/날짜 등)가 변수 칸에 자동 세팅되도록 연동.
  - 어색했던 `"📝 현재 입력어를 자유 메모로 바로 담기"` 버튼 문구를 **`"+ 검색창에 적은 문구 그대로 추가"`**로 직관화.
- **답변조합기 제목·본문 문장·한글 IME 조합 중 검색 개선 ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs))**:
  - 한글 조사(`은/는/이/가/을/를/으로` 등) 자동 제거 매칭, 한글 입력 중 마지막 자음(`ㄱ~ㅎ`) 초성 프리픽스 매칭, 여러 어절 문장 입력 시 부분 일치(OR 합산) 폴백을 추가해 단축 키워드(`ds`)뿐만 아니라 제목이나 문장 검색이 끊김 없이 동작하도록 개선.
- **추천 알약 `{}` 변수 하단/테두리 강조 및 `\` 명령키 안내 분리 ([SuggestionBar.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**:
  - 텍스트 `{}` 표기 대신 **두꺼운 하단/테두리 강조(`#F59E0B`, 하단 3.5px)**로 변수 포함 알약을 시각적으로 즉시 구분.
  - 인식 헤더 바로 아래에 **`명령키 [ \ ] 누른 뒤 ➔ 1번 [Tab] · 번호 선택 [1~9]`** 안내 줄을 배치하고, 개별 알약에는 `\+` 반복 없이 **`[Tab] 제목`**, **`[2] 제목`**, **`[3] 제목`**으로 간결하게 표시.
- **UI 테마와 `ChatGENIE` 메모장 강조 색상 통합 및 실시간 미리보기 추가 ([ThemeManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ThemeManager.cs), [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs))**:
  - 별도 텍스트 라디오 버튼 대신 **UI 통합 테마 컬러** 하나로 통합하되, `ChatGENIE` 메모장은 선택한 테마보다 한 단계 더 선명하게 강조된 팔레트(`GetEmphasizedMemoPalette`)가 자동 적용되도록 개편하고 우측 실시간 예시 화면에 메모장 미리보기 카드를 추가.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.5.4] — 2026-11-28 (코드 품질·보안 감사 및 스파게티 중복 로직 정리 · 컴파일러 경고 0건 달성)

### 🐛 Fixed
- **`\+M` 이전 단어(`_lastNonSlashWord`) 잔류 버그 차단 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `\` 뒤에 일반 단축어(`\dz`)가 이어지거나 템플릿 치환 완료(`CompleteSelection`), `Enter`/`Esc`/`Delete`/방향키 입력, 외부 창 전환, `Ctrl`/`Alt`/`Win` 조합키 입력 시 `_lastNonSlashWord`와 `_memoSlashArmed`가 즉시 초기화되도록 보강하여 과거 단어가 뒤늦게 담기는 현상 원천 차단.

### 🧹 Removed / Refactored (스파게티 코드 방지 및 경량화)
- **키훅 내 중복 메모 단축키 분기 통합 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `HookCallback`과 `TryHandleCompletionTrigger` 두 곳에 중복 존재하던 `\+M`, `\+V`, `\+C`, `\+F`, `\+N` 분기를 `HookCallback` 단일 경로로 통합(약 65줄 중복 제거).
- **미사용 이벤트·필드 정리 및 컴파일러 경고 12건 → 0건 달성 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs), [QuickSearchWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml.cs))**:
  - `CS8604`(null 참조 가능성), `CS4014`(비대기 비동기 호출), `CS0067`/`CS0414`(폐기된 이벤트 및 미사용 필드) 전량 정리.
- **`publish/` 중복 OCR 모델 폴더(`models\v5`, 13.7MB) 자동 제거 ([ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj))**:
  - `Assets\Ocr`의 한국어 PP-OCRv5 모델만 유지하고 NuGet 기본 라틴어 중복 모델 폴더는 배포 시 자동 삭제되도록 개선.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.5.3] — 2026-11-28 (`\+M` 단어담기 버그 수정 · 메모장 색상 설정 추가 · 알약 `{}` 변수 표시)

### 🐛 Fixed
- **`\+M` (`\+ㅡ`) 단어 담기 미동작 해결 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - `\` 입력 시 백그라운드 워커가 `_buffer.Clear()`를 먼저 실행해 방금 입력한 단어가 지워지던 타이밍 문제를 `_lastNonSlashWord` 보존으로 해결하고, `\`와 `M` 사이에 `Shift`·`한/영` 키가 눌려도 대기 상태가 풀리지 않도록 보강.
  - 방금 입력한 단어가 비어 있을 때도 무응답 대신 안내 배지를 표시하도록 개선.

### ✨ Added / Changed
- **메모장 색상 설정 추가 ([AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs), [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs))**:
  - 제어판 > 디자인 탭에 **ChatGENIE 메모장 색상** 6종(`연노랑(기본)`, `화이트`, `스카이블루`, `민트`, `라벤더`, `피치핑크`) 선택 옵션 추가 및 저장 시 즉시 반영.
- **추천 알약 `{}` 변수 포함 표시 ([SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**:
  - 템플릿 본문에 `{송장번호}` 등 `{}` 자리표시자가 포함된 경우 추천 알약에 `{}` 표시 및 변수 목록 툴팁을 함께 제공.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.5.2] — 2026-11-28 (`ChatGENIE` 메모창 UI 개편 · 날짜 자동 판별 보정 · 변수 타입 수동 변경 · OCR 로딩 표시)

### ✨ Added / Changed
- **메모창 UI/UX 개편 ([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs))**:
  - 상단 헤더에서 핀 아이콘(`📌`)과 `임시메모` 및 `Xenie` 버튼을 제거해 **`ChatGENIE`** 단독 표기로 정리하고, **`📋 붙여넣기`** · **`🧹 지우기`** 버튼을 동일 디자인으로 나란히 배치.
  - 창 너비를 `360px` → **`430px`**로 넓히고, 전체 배경·테두리를 보다 부드러운 연노랑(`#FFFDF8`, `#FEF9E7`, `#E8D284`) 톤으로 조정.
  - 하단 단축키 안내에서 `조합기(\+N)`를 분리 제거.
  - `오늘`·`내일`·`금일 오후`·`내일 오전` 빠른 날짜 버튼을 **입력 줄 옆 드롭다운(`📅 날짜 ▾`)**으로 통합.
  - **OCR 로딩 표시(`⏳ 인식중`)**: `\+V` 또는 붙여넣기 실행 시 헤더 옆에 모래시계 아이콘이 표시되었다가 완료 후 사라지도록 구현 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs)).
  - **변수 타입 수동 변경**: 카드 왼쪽 배지(`📦 송장`, `📅 날짜`, `💰 금액`, `📝 메모`) 클릭 시 드롭다운 메뉴로 타입을 즉시 전환할 수 있도록 추가.

### 🐛 Fixed
- **날짜/시간 문자열이 `송장`으로 오인식되던 문제 해결 ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs))**:
  - `2026-11-27 22:27:44` 같은 날짜·시간 포맷을 `송장`보다 먼저 판별해 **`📅 날짜`**로 정확히 분류되도록 수정.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.5.1] — 2026-11-27 (`ChatGENIE` 전체 브랜드 개편)

### ✨ Added / Changed
- **프로그램 전체 명칭을 `ChatGENIE`로 통일 ([ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj), [AppConstants.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Constants/AppConstants.cs))**:
  - 기존 `답정너` / `DapJeongNer` / `DapJNR` 명칭을 메인 창, 로그인 창, 제어판, 임시메모(`ChatGENIE 임시메모`), 트레이 메뉴/풍선 알림, 단축키 검증 안내, 종료 확인 창(`ChatGENIE 종료` / `ChatGENIE를 종료할까요?`), `pack://` 리소스 URI 및 출력 실행 파일([ChatGENIE.exe](file:///C:/Users/taerin/Desktop/채팅도우미/publish/ChatGENIE.exe))까지 일괄 변경.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.5.0] — 2026-11-27 (OCR 2단계 개편: PaddleOCR PP-OCRv5 한국어 ONNX 로컬 엔진 탑재 & 종료 창 브랜드명 수정)

### ✨ Added / Changed
- **OCR 2단계 엔진 업그레이드 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs), [ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj))**:
  - `RapidOcrNet 4.2.0` + **PaddleOCR PP-OCRv5 한국어 ONNX 모델**(`Assets/Ocr/korean_PP-OCRv5_rec_mobile_infer.onnx`, `ppocrv5_korean_dict.txt`, `ch_PP-OCRv5_mobile_det.onnx`, `ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx`)을 1순위 로컬 OCR 엔진으로 탑재.
  - 작은 엑셀/표 글씨(11–13pt)에서 `28.5Ah`, `(KTX6핀)`, `(21X)`, `(29X)`, `배달가방+거치대셋트` 등 한·영·숫자·소수점·괄호 혼합 인식률을 대폭 개선.
  - 모델 누락 또는 예외 발생 시 기존 Windows 내장 OCR(`Windows.Media.Ocr`)을 2순위 자동 폴백으로 유지.

### 🐛 Fixed
- **종료 확인 창 구버전 명칭 수정 ([ExitConfirmWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ExitConfirmWindow.xaml))**:
  - 창 제목 `"ChatHelper 종료"` → **`"답정너 (DapJNR) 종료"`**, 본문 `"ChatHelper를 종료할까요?"` → **`"답정너를 종료할까요?"`** 로 수정.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.4.4] — 2026-11-27 (OCR 1단계 개선: 고품질 선확대 · 다크모드 반전 · 단어 단위 한/영 교차 검증)

### ✨ Added / Changed
- **OCR 전처리 파이프라인 고도화 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - 기존 1x 해상도 이진화 대비 보정이 소수점(`.`)과 작은 글자 획(`트`, `X`)을 뭉개는 문제를 개선하기 위해, **고품질 선확대(`BitmapScalingMode.HighQuality`, 최대 3배)**를 먼저 수행한 뒤 약한 Unsharp 선명화(15%)와 소프트 대비 보정을 적용하도록 순서 개편.
  - 어두운 배경 캡처 자동 감지 및 흑백 반전(`isDarkBg`) 추가.
- **단어 단위 한(`ko`)/영(`en`) 바운딩 박스 교차 검증 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - 줄 단위 비교 외에 `OcrLine.Words` 좌표 기반으로 한·영 혼합 줄에서도 소수점(`28.5Ah`), 영문/숫자 토큰(`ZOOM`, `KTX`), 한자 오인식(`丁` → `T`)을 영문 엔진 결과로 자동 보정.
- **작은 엑셀 표 혼합 텍스트 OCR 통합 테스트 추가 ([AiAndOcrIntegrationTests.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper.Tests/AiAndOcrIntegrationTests.cs))**.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.2.0] — 2026-11-27 (`Xenie` · `챗헬프 임시메모` 리브랜딩, OCR 줄바꿈/숫자 보정, 자석 도킹 & 페이퍼로지 폰트)

### ✨ Added / Changed
- **리브랜딩 (`챗헬프 임시메모` & `Xenie`) 및 멀티윈도우 최상단(`Topmost`) 유지 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs))**:
  - `포스트잇 메모` → **`챗헬프 임시메모`**, `스무고개 답변조합기` → **`Xenie`** 로 명칭 통일.
  - 마스터창(`MainWindow`) 및 `Xenie` 창에 Win32 `EnforceTopmost`(`WS_EX_TOPMOST` + `HWND_TOPMOST`)를 적용해 멀티 모니터/타 프로그램 전환 시에도 항상 최상단에 유지되도록 개선.
  - `Xenie` 창이 이미 열려 있을 때 재호출 시 화면 중앙 이동·포커스 강탈 없이 **현재 위치를 유지하며 테두리 하이라이트만 표시**하도록 개선.
- **자석 도킹(화면 붙이기) & 가장자리 스냅 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))**:
  - `🔗 임시메모 붙이기` 버튼 또는 창 테두리 22px 근접 이동 시 `챗헬프 임시메모`와 `Xenie`가 자석처럼 붙어 함께 이동하며, 모니터 좌/우/상/하 가장자리 18px 스냅을 지원 (전역 마우스 훅 미사용으로 고주사율 모니터 무지연 유지).
- **비개발자 친화적 변수 UX & 여러 줄 카드 나누기 ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs))**:
  - 임시메모 카드의 `{1}`, `{2}` 배지를 **`📦 송장`**, **`📅 날짜`**, **`💰 금액`**, **`📝 메모 N`** 이름표로 자동 표시하고, `{송장번호}`/`{날짜}`/`{금액}` 변수에 의미 기반 자동 매칭 및 `Xenie` 변수 칸 원클릭 알약 선택 지원.
  - 여러 줄 OCR/텍스트 메모 카드에 **`줄나누기`** 버튼 추가(기본 1개 카드로 줄바꿈 유지 저장, 필요 시 1클릭 분리).
- **페이퍼로지(`Paperlogy`) 폰트 전역 내장 ([ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj), [App.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/App.xaml))**:
  - `Paperlogy` 4종(`Regular`, `Medium`, `SemiBold`, `Bold`)을 앱 리소스로 내장해 별도 폰트 설치 없이 전 창에 일괄 적용.

### 🐛 Fixed
- **OCR 줄바꿈 소실 및 송장번호 한자/한글 오인식 해결 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - `ocrResult.Lines` Y좌표 정렬을 통해 표/여러 줄 캡처 시 줄바꿈이 그대로 보존되도록 수정.
  - 그레이스케일 대비 보정 + 한(`ko`)/영(`en`) 이중 OCR 엔진 교차 검증을 도입해 숫자/송장번호(`0-` → `堡`, `우`, `수` 등) 깨짐 현상 해결.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.4.3] — 2026-11-27 (한글 IME 단축키 선점 강화 · `\+M` 긴 구문 버퍼 보존 및 키 누수 차단)

### 🐛 Fixed
- **한글 IME 조합 중(`VK_PROCESSKEY`) 단축키 스캔코드 직접 매핑 폴백 추가 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `MapVirtualKey`가 실패하는 환경에서도 하드웨어 스캔코드(`M`/`V`/`C`/`F`/`N`/`\`/`Tab`/`S`/`X`/`T`)로 답정너 단축키를 우선 가로채도록 보강.
- **`\ + M` (`\ㅡ`) 단어 담기 실패 및 `\m` 채팅창 잔류 수정 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `VK_SPACE` 버퍼 유지 길이를 15자 → 40자로 확장해 2~3어절 구문도 끊기지 않고 메모에 담기도록 수정.
  - `\+M` 입력 시 `token` 유무와 관계없이 `M` 키를 항상 가로채(`return (IntPtr)1;`) 채팅창에 `\m`이 찍히거나 `m`만 메모에 들어가는 현상을 차단.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.4.2] — 2026-11-27 (임시메모 세로 760px 확대 · 2줄 단축키 바 · 전체 UI `기능(단축키)` 간결화)

### ✨ Added / Changed
- **임시메모 창 세로 크기 확대 ([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs))**:
  - 스무고개 조합기(`Xenie`, 760px)와 동일한 **세로 760px**(화면 높이에 맞춰 자동 보정)로 확대해 여러 메모 카드를 한눈에 볼 수 있도록 개선.
  - 하단 안내 영역을 **위아래 2줄**로 넓혀 `열기/닫기(Alt+N) · 단어담기(\+M)` / `클립보드(\+V) · 비우기(\+C) · 조합기(\+N)` 문구가 잘리지 않도록 수정.
- **전체 UI 설명 문구 간결화 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [QuickSearchWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml), [AppConstants.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Constants/AppConstants.cs))**:
  - 장황한 문장형 설명을 제거하고 **`기능명(단축키)`** 형식(예: `복사(Ctrl+C)`, `입력(Enter)`)으로 일관되게 정리.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.4.1] — 2026-11-27 (OCR 한/영 정제 · Win11 메모장 리본 팁 방지 · `\+M`/`\+V` 타이밍/중복 차단 · 임시메모 UI 개선)

### 🐛 Fixed
- **OCR 한자/깨진 기호 제거 및 한글·영문·숫자 중심 정제 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - `FilterKoreanAndEnglishOnly`를 추가해 `싯포스二>`, `셋ㅁ` 같은 한자·단독 자모 깨짐을 제거하고 한글·영문·숫자·기본 문장부호만 깔끔하게 추출하도록 수정.
- **메모장 등 자동완성 시 Windows 11 리본 단축키 팁(`F`, `E`, `V`...)이 뜨던 현상 해결 ([TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs))**:
  - `FastPasteText`가 `Alt` 키가 눌려 있지 않을 때도 무조건 `VK_MENU` `keyUp`을 전송해 Win11 메모장 리본 메뉴가 활성화되던 문제를 실제 `Alt` 눌림 여부 확인 후 해제하도록 수정.
- **`\ + M` 단어 담기 실패 & `\ + V` 시 직전 자동완성 알약이 담기던 문제 해결 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - `_memoSlashArmed`가 후보 유무(`_hasCandidates`)와 무관하게 우선 처리되도록 수정해 `\+M`이 채팅창으로 새어나가지 않도록 개선.
  - `\+V` 실행 시 `GetClipboardSequenceNumber()`로 직전 자동완성 붙여넣기(`LastInjectedClipboardSeq`)와 동일한 텍스트는 제외해 알약 문구가 임시메모에 들어가지 않도록 차단.
- **임시메모 UI 개선 ([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml))**:
  - 헤더의 `붙이기` 버튼을 제거(자석 도킹은 숨은 기능으로 유지), 닫기(`✕`) 버튼을 `32×30` 크기로 확대, 창 너비(`360px`) 및 하단 바 줄바꿈(`TextWrapping="Wrap"`) 적용으로 하단 설명 잘림 해결.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.4.0] — 2026-11-27 (`답정너 · DapJeongNer / DapJNR` 브랜드 개편 & 구형 파일 정리)

### ✨ Added / Changed
- **브랜드명 및 실행 파일명 개편 (`ChatHelper` → `답정너 · DapJeongNer / DapJNR`)**:
  - [ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj) `<AssemblyName>DapJeongNer</AssemblyName>` 적용으로 빌드 출력 실행 파일을 [publish/DapJeongNer.exe](file:///C:/Users/taerin/Desktop/채팅도우미/publish/DapJeongNer.exe)로 전환.
  - 도킹 바(`MainWindow`), 제어판(`ControlPanelWindow`), 로그인(`LoginWindow`), 임시메모(`StickyMemoWindow`), 트레이 아이콘(`TrayIconService`), 단축키 안내(`HotkeyValidator`) 등 사용자 화면 전체를 **`답정너 (DapJNR / DapJeongNer)`**로 통일.
  - 모든 WPF 폰트/리소스 `pack://` URI를 `DapJeongNer;component`로 일괄 갱신.

### 🗑️ Removed
- 완료된 구형 계획서 및 구버전 배치 파일 정리: `REFACTORING_AND_IMPROVEMENT_PLAN.md`, `docs/PLAN_v3.3.md`, `run.bat`, 구 실행 파일 `publish/ChatHelper.exe`.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.3.0] — 2026-11-27 (임시메모 입력/자석 도킹 분리 버그 수정 · 변수 클립보드 정제/OCR `&` 보정 · 미리보기 되돌리기/비우기 & 날짜 프리셋)

### ✨ Added / Changed
- **`Xenie` 최종 완성 문장 미리보기 `↩ 되돌리기` · `🧹 비우기` 버튼 추가 ([ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))**:
  - 직접 수정하다가 망쳤을 때 자동 조합 원본으로 즉시 복구하는 `↩ 되돌리기` 및 내용만 비우는 `🧹 비우기` 버튼 추가.
- **임시메모 · Xenie 단축키 안내 강화 ([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml))**:
  - 하단 바 및 마스터창 버튼 툴팁에 `Alt+N`, `\+M`, `\+V`, `\+C`, `\+F`, `Ctrl+Shift+Enter` 등 핵심 단축키를 상시 노출.
- **기본 날짜 변수 원클릭 프리셋 추가 ([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))**:
  - `📅 오늘`, `📅 내일`, `🕒 금일 오후`, `🕒 내일 오전` 원클릭 프리셋 버튼을 임시메모 상단과 `Xenie` 날짜 계열 변수 칸 옆에 추가.

### 🐛 Fixed
- **자석 도킹 분리 후 재스냅 방지 쿨다운(1.5초) & 명확한 `🔗 붙이기`/`🔓 분리` 버튼 적용 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml))**:
  - 임시메모와 `Xenie`를 분리한 직후 다시 달라붙던 문제를 1.5초 쿨다운(`_dockCooldownUntil`)과 `IsVisibleChanged` 정렬 동기화로 해결.
- **임시메모 `[입력]` 버튼 클릭 시 대상 채팅창 미입력 수정 ([StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs))**:
  - 유효 대상 창 폴백(`_targetHwnd` → `GetForegroundWindow()` → `_hookService.LastTargetHwnd`) 및 안내 배지 추가, `FastPasteText` 포커스 대기시간 안정화(`110ms`).
- **변수 `📋` 클립보드 다중줄/UI 잡음 정제 및 OCR `&` 오인식 보정 ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs), [OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - `송장\n입력\n456-8280-8146...`처럼 여러 줄/UI 단어가 섞여 들어갈 때 의미 패턴에 맞는 단일 줄만 정제 추출(`SanitizeClipboardForVariable`).
  - 숫자 사이 `0-`가 `&`로 뭉개지는 OCR 오인식(`456-828&8150` → `456-8280-8150`) 자동 보정(`NormalizeNumericOcrArtifacts`).
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.1.0] — 2026-11-27 (초경량 포스트잇 메모지 `StickyMemoWindow` ↔ 스무고개 답변 조합기 2-트랙 분리)

### ✨ Added / Changed
- **초경량 포스트잇 메모지([StickyMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml), [StickyMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/StickyMemoWindow.xaml.cs)) 신설**:
  - 채팅 화면 구석을 가리지 않는 작은 플로팅 포스트잇(`330×370`)으로, `\+M` / `\+V`(이미지 OCR 포함) / 알약 `+ 메모` / 직접 입력 / `📋` 클립보드 담기로 모은 조각 메모(`{1}`, `{2}`)를 가볍게 보관.
  - 카드 더블클릭/`입력` 버튼으로 즉시 채팅창 입력, **마우스 드래그 앤 드롭**으로 스무고개 조합기 `{변수}` 칸에 끌어다 놓기 지원.
  - `Alt+N` 또는 마스터창 **`메모지 (N)`** 버튼으로 즉시 열기/닫기, 투명도(`MemoOpacity`) 적용.
- **스무고개 답변 조합기([ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))와의 역할 분리 및 연동**:
  - 마스터창 **`스무고개`** 버튼, `\+N`, 포스트잇 내 **`🧩 스무고개`** 버튼으로 호출하며, 포스트잇 메모지에 모아둔 값들을 `{송장번호}`/`{1}` 변수에 자동 공급하거나 드래그 앤 드롭으로 바로 결합 가능.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.0.4] — 2026-11-26 (마스터창 배지 잘림 수정 · `+ 메모`/`\+M` 입력 단어 원문 보존 · 미리보기 2배 확대)

### 🐛 Fixed
- **마스터창 동기화·메모 알림 배지 글씨 잘림 수정 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml))**:
  - `SizeToContent="Width"` (`MinWidth="760"`)를 적용해 우측 배지(`BadgeNotice`) 표시 시에도 버튼과 글씨가 잘리지 않고 자연스럽게 확장되도록 수정.
- **알약 제안 중 `+ 메모` / `\ + M` 시 제안 알약이 들어가고 단어가 잘리던 현상 수정 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**:
  - `\` 입력이 버퍼를 지워 `"단어거 잘림"`이 `"단"`으로 잘리던 문제를 차단하고, 알약 제안 여부와 무관하게 사용자가 입력한 단어 원문 그대로를 자유 메모에 추가하도록 수정.
- **최종 완성 문장 미리보기 높이 2배 확대 ([ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml))**:
  - 창 높이를 `760px`, 미리보기 입력란(`TxtFinalPreview`) 높이를 `86px` → `175px`로 확대.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.0.3] — 2026-11-26 (상담 메모 · 답변 조합기 단일 창 완전 통합 & 변수 드래그 앤 드롭 · 날짜 스마트 입력 타입)

### ✨ Added / Changed
- **상담 메모 HUD(`ConsultationMemoHud`)와 조합기(`ConsultationMemoWindow`) 단일 창 완전 통합 ([ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - 중복되던 2개의 메모 창을 [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml) 하나로 통합하여 `Alt+N` · `\+N` · 마스터창 `메모열기 (N)` · `\+M` · `\+V` · `\+C` · `\+F` · `Ctrl+Shift+Enter`가 모두 동일한 단일 창과 데이터를 공유하도록 일원화.
  - 채팅 중 `\+M` / `\+V`로 담은 메모는 창을 강제로 띄우지 않고 백그라운드에서 누적되며, 마스터창 `[메모열기 (N)]` 버튼에 실시간 개수가 표시됨.
- **변수 입력 마우스 드래그 앤 드롭 & 변수명 기반 스마트 입력 타입 추가 ([ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))**:
  - 우측 메모 카드 또는 외부 창(브라우저·메모장 등)의 텍스트를 마우스로 드래그해 `{송장번호}` 등 변수 입력 칸에 바로 놓기(`AllowDrop`) 지원.
  - `{날짜}`, `{예약일}` 등 날짜 관련 변수에는 달력(`DatePicker`)과 `[오늘]` 원클릭 버튼이 자동으로 함께 표시되도록 개선.

### 🗑️ Removed
- 중복 창 [ConsultationMemoHud.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoHud.xaml) / `ConsultationMemoHud.xaml.cs` 제거.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.0.2] — 2026-11-26 (송장번호 2중 붙여넣기 차단 · 작은 이미지 OCR 3배 보정 · 단축키 교차/OS 충돌 감지 · 알약 헤더 비례 확대 · 조합기 방향키/변수 입력 개선)

### ✨ Added / Changed
- **상담 메모 조합기(`ConsultationMemoWindow`) `↑`/`↓` 방향키 선택 및 `{변수}` 전용 입력 칸 추가 ([ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs))**:
  - `Alt+S` 검색창과 동일하게 `↑`/`↓` 방향키로 좌측 후보 목록을 이동하고 `Enter`로 즉시 담기 지원.
  - 담은 답변에 `{송장번호}`, `{날짜}` 등의 자리표시자가 감지되면 하단에 **전용 변수 입력 칸(`VariableInputsBar`)과 📋 클립보드 채움 버튼**을 자동 생성해 직관적으로 치환 가능.
- **추천 알약 상단 헤더(인식 텍스트 · 타이머 · `+ 메모` · `✕`) 비례 확대 ([SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**:
  - 알약 크기(`1.0x~5.0x`)에 맞춰 상단 인식 배지, 타이머, `+ 메모`, `✕` 닫기 버튼도 동일 비율로 커지도록 개선.

### 🐛 Fixed
- **송장번호 변수 치환 시 방금 붙여넣은 템플릿 본문이 2번 겹쳐 들어가던 문제 해결 ([TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs), [ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs))**:
  - `FastPasteText`가 붙여넣기 위해 클립보드에 넣었던 본문(`LastInjectedText`) 및 줄바꿈이 포함된 긴 텍스트는 `{송장번호}` 자동 폴백 대상에서 제외.
- **작은 1줄 캡처 이미지(`\ + V`) OCR 미인식 수정 ([OcrService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/OcrService.cs))**:
  - 높이가 작은 1줄 캡처(`h < 60` 또는 `w < 220`)는 **3배 확대 + 24px 흰색 여백(Padding)**을 추가해 Windows OCR이 숫자·코드를 정확히 인식하도록 개선.
- **단축키 충돌 검사 누락 수정 ([HotkeyValidator.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/HotkeyValidator.cs), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs))**:
  - 기본 단축키(`Alt+S`) 예외를 제거하고, 템플릿 검색 단축키와 메모 열기 단축키 간의 내부 중복 및 OS `RegisterHotKey` 실시간 선점 검사가 모두 동작하도록 수정.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.0.1] — 2026-11-26 (메모 열기 단축키·버튼, 클립보드 이미지/자리표시자 자동 결합, `+ 메모` 가독성, `\+C` 수정, 알약 4~5배 마우스 고정)

### ✨ Added / Changed
- **상담 메모 HUD 전용 열기 단축키(`Alt+N`, 제어판 변경 가능) & 마스터창 `[메모열기]` 버튼 추가 ([AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs), [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [ConsultationMemoHud.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoHud.xaml.cs))**:
  - 메모가 0개일 때도 `Alt+N` 또는 마스터창 도킹 바의 `메모열기` 버튼 클릭으로 즉시 메모 HUD를 열고 닫을 수 있도록 개선.
- **상담 메모 HUD 투명도(`MemoOpacity`) 독립 설정 추가 ([ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml))**:
  - 제어판 `🎨 디자인 & 표시`에서 메모 HUD 투명도(`70%` / `80%` / `90%` / `100%`)를 별도로 설정하고 즉시 반영.
- **추천 알약 크기 `4.0x` / `5.0x` 추가 및 마우스 커서 위치 자동 고정 ([SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs))**:
  - 기존 `1.0x~3.0x`에 `매우 크게(4.0x)`·`최대(5.0x)`를 추가하고, `4.0x` 이상 선택 시 채팅창을 가리지 않도록 등장 위치를 반드시 `[마우스 포인터 위치]`로 고정.
- **송장번호 등 복사 후 메모창 없이도 템플릿 자리표시자(`{송장번호}`, `{1}` 등) 즉시 자동 결합 ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - 메모가 비어 있을 때도 클립보드(텍스트 및 이미지 OCR) 값을 읽어 `{...}` 자리표시자에 바로 채워 완성할 수 있도록 개선.

### 🐛 Fixed
- **`+ 메모` 버튼 흰색 글씨 가독성 수정 ([SuggestionBar.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml))**: 파스텔 배경 위에서 보이지 않던 `#FFFFFF` 글씨를 `ThemeAccentText` + `ThemeAccentBorder` 스타일로 수정.
- **`\ + C` 메모 초기화 미작동 수정 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**: `\` 직후 대기 큐 정리 및 `_hasActiveMemos` 상태와 무관하게 `\+C`가 즉시 초기화를 수행하도록 수정.
- **`Win + V` / 캡처 이미지 클립보드 인식 누락 수정 ([ClipboardSmartService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ClipboardSmartService.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**: `\+V` 및 HUD `📋 클립보드 담기`가 텍스트 전용 함수 대신 `ClipboardSmartService`(PNG/DIB 이미지 OCR + 텍스트)를 사용하도록 개선.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v3.0.0] — 2026-11-26 (비방해형 스무고개 메모 HUD · 양방향 번역 세션 · 배포 보안 강화)

### ✨ Added / Changed
- **비방해형 스무고개 상담 메모 HUD (`ConsultationMemoHud`) & 키보드 전용 단축키 ([ConsultationMemoHud.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoHud.xaml), [ConsultationMemoHud.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoHud.xaml.cs), [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `Alt+S` 검색창과 동일한 **744×540 크기**의 깔끔한 카드 목록 HUD로 설계해 누적 키워드 칩·개별 메모 카드(`▲`/`▼` 순서 이동 & `{1}`/`{2}` 인덱싱 배지)·조합 형식(`문단`/`줄바꿈`/`번호`/`불릿`)·**직접 수정 가능한 최종 답변 미리보기** 및 **수정 답변 시트 등록 확인(`💾 시트 등록`)**을 제공.
  - **`\ + V`** (또는 HUD `📋 클립보드 담기`): 복사해 둔 송장번호·텍스트를 `{변수}`용 자유 메모로 즉시 추가.
  - **한글 IME 백스페이스 잔상 및 숫자 오인식 수정** ([TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs), [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)): `SendBackspaces` 실행 전 `VK_RIGHT`로 조합 중 한글 IME를 확정해 긴 문장(`전원이 안켜져요`) 메모 추가 시 앞글자가 남지 않도록 수정하고, 3자리 이상 숫자/코드 입력 후 `\+M` 시 엉뚱한 제시 알약 대신 `{1}` 자유 메모로 등록되도록 개선.
  - **구글 시트(`채팅도우미환경변수(CS)` > `채팅템플릿`) 동기화**: 기존 `td`/`td2`의 `[송장번호 입력]`을 `{송장번호}`로 갱신하고, `{날짜}`/`{모델}`/`{금액}`/`{내용}` 자리표시자 및 `블록` 카테고리 조각 템플릿 6종(`as_visit`, `rf_fee`, `b_delay`, `b_gift`, `b_check`, `b_end`)을 추가 반영.
  - Windows 음소거 단축키(`Alt+M`) 충돌을 제거하고, 채팅 흐름을 끊지 않는 **역슬래시(`\`)+문자** 및 `Ctrl+Shift+Enter` 키보드 워크플로우로 재설계.
  - **`\ + M`** (또는 제안 알약 헤더 **`+ 메모`** 버튼): DB 일치 템플릿뿐 아니라 **미인식(`[미일치]`) 자유 키워드도** 별도 플로팅 HUD에 즉시 달아두고, 채팅창에 입력했던 단어를 자동으로 지워(백스페이스) 대화를 깔끔하게 이어갈 수 있음.
  - **`\ + C`**: 누적된 상담 메모를 키보드로 일괄 초기화.
  - **`\ + F`** 또는 **`Ctrl + Shift + Enter`**: 누적된 메모 이력(또는 스무고개 좁히기 일치 템플릿)을 최종 완성 문장으로 채팅창에 한 번에 입력. 템플릿 본문의 `{메모}` · `{1}` · `{날짜}` 등 자리표시자에 자유 메모 값을 자동 치환함.
  - **`\ + N`** (또는 마스터창 `메모` 우클릭 / HUD `편집` 버튼): 상세 편집기([ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml)) 호출.
  - **마스터창 & 제어판 ON/OFF 토글**: [MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml) 도킹 바 `메모 ON/OFF` 버튼 및 제어판 체크박스(`EnableConsultationMemo`, 테스트 기본값 `ON`)로 일반 사용자와 상담사가 손쉽게 켜고 끌 수 있음.

### 🐛 Fixed
- **양방향 번역 재전환 버그 수정 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [TextInjectionService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TextInjectionService.cs))**:
  - `한→영` 번역 후 `⇄ 영→한 다시 번역` 클릭 시 한글 원문을 재전송해 `한글 ↔ 한글`로 나오던 문제를 **인메모리 `TranslationSession`** (`KoreanText` / `EnglishText` 임시 보관)으로 해결해 항상 반대 언어 원문으로 정확히 역번역.
  - `CaptureSelectedText`에 `GetClipboardSequenceNumber()` 검증을 추가해 드래그 선택이 복사되지 않았을 때 이전 클립보드 데이터가 오번역되는 현상 차단.

### 🔒 Security
- **배포(`publish/`) 개인 ID · 설정 · 로그 유출 원천 차단 ([ConfigManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConfigManager.cs), [SecretVault.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/SecretVault.cs), [AppLogger.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/AppLogger.cs), [ChatHelper.csproj](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/ChatHelper.csproj))**:
  - **경로 분리**: `config.json`은 `%LocalAppData%\ChatHelper\`, 일자별 로그는 `%LocalAppData%\ChatHelper\logs\`로 분리 저장해 `publish/` 폴더에 개인 데이터가 절대 남지 않도록 개선.
  - **DPAPI 아이디 암호화**: `KeepLoggedIn == false`이면 디스크에 아이디를 저장하지 않으며, `true`일 때도 Windows DPAPI(`CryptProtectData`, 현재 Windows 사용자 계정 전용)로 암호화된 `EncryptedUserId`만 저장(평문 `CurrentUserId`는 `[JsonIgnore]`).
  - **7일 달력 기준 로그 자동 삭제(방안 C)** & 민감정보 마스킹: 현재 날짜 기준 7일이 지난 로그 파일은 자동 삭제되며, 로그인 ID·번역 원문/결과·고객 대화 내용은 디스크 로그에 기록하지 않음.
  - **빌드 자동 정리**: `ChatHelper.csproj` `AfterTargets="Publish"` 타겟이 `publish/` 내 `config.json`, `*.log`, `*.csv`, `.env`, `*.tmp`를 자동 삭제.
- **GAS 재배포 필요 여부**: 불필요.

---

## [v2.5.0] — 2026-11-26 (한↔영 양방향 번역 선택 & 스무고개 상담 메모 조합기 `Alt+M` 추가)

### ✨ Added / Changed
- **양방향 Google 번역 선택 지원 (`한글 → 영문` / `영문 → 한글` / `자동 감지`) ([AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs), [DataSourceResolver.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/DataSourceResolver.cs), [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - **제어판 > 단축키 & 동작**에서 번역 방향을 `🔄 자동 감지 (한글 ↔ 영문)`, `🇰🇷 → 🇺🇸 한글 → 영문`, `🇺🇸 → 🇰🇷 영문 → 한글` 중 선택할 수 있도록 추가.
  - `Alt+T` 드래그 번역 결과 카드(`SuggestionBar`)에서도 방향에 맞는 라벨(`영문 원문` ↔ `Google 한글 번역` / `한국어 원문` ↔ `Google 영문 번역`)과 **`⇄ 영→한 / 한→영 다시 번역`** 원클릭 전환 버튼을 제공해 즉시 재번역 가능.
- **스무고개 키워드 메모 & 최종 답변 조합기 (`Alt+M`) ([ConsultationMemoService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ConsultationMemoService.cs), [ConsultationMemoWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml), [ConsultationMemoWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ConsultationMemoWindow.xaml.cs), [MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - 상담 중 중간중간 답변해야 하는 상황에서 스무고개처럼 키워드를 `Enter`로 하나씩 칩(`🏷 키워드`)으로 누적해 DB 후보를 단계적으로 좁히고(초성/부분일치 AND 검색), 필요한 템플릿 답변이나 자유 메모를 우측 **상담 메모 보드**에 순서대로 달아둘 수 있는 전용 창 추가.
  - 창을 닫아도(`Esc`) 상담 중 모은 메모가 유지되며, 연결 형식(`문단 구분`, `줄바꿈`, `번호 매기기 1. 2. 3.`, `불릿 •`)을 선택해 실시간 미리보기 후 **`✅ 최종 완성 입력 (Ctrl+Enter)`** 또는 **`📋 전체 복사`**로 한 번에 완성 전송 가능.
  - 메인 HUD 도킹 바 `메모` 버튼 및 전역 단축키 `Alt+M`으로 즉시 호출 가능.
- **단위 테스트 확장 ([TemplateManagerTests.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper.Tests/TemplateManagerTests.cs))**:
  - `ResolveTranslationPair` (`Auto`, `KoToEn`, `EnToKo`) 및 `ConsultationMemoService` 다중 키워드·초성 좁히기 필터링, 최종 문장 조합(`Numbered`, `Bulleted`) 검증 추가.
- **GAS 재배포 필요 여부**: 불필요 (기존 `action=translate`의 `from`/`to` 파라미터 활용 및 클라이언트 기능 추가).

---

## [v2.4.3] — 2026-11-24 (`Alt+Q` 전역 핫키 등록 누락 보완 & 간헐적 채팅 타이핑 지연 해결)

### 🐛 Fixed
- **간헐적 채팅 타이핑 지연 및 저수준 키훅 타임아웃 원인 제거 ([KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs))**:
  - `HookCallback`(`WH_KEYBOARD_LL`)에서 일반 채팅 중 `Space`·`A`(`ㅁ`)·`E`(`ㄷ`)·숫자키 입력 시마다 `_keyChannel`을 동기 플러시하며 `lock (_lock)` 및 후보 탐색(`EvaluateCandidates`)을 실행해 간헐적 키 입력 렉과 OS 훅 타임아웃을 유발하던 문제를 수정.
  - `ShouldHandleTriggerInHook`을 도입해 실제 완성 트리거(`\`, 후보 표시 중 `Tab` / `Shift+Space` / `\` 이후 `1~9`·`A`·`E`)일 때만 채널 플러시와 `_lock` 진입을 허용하고, `IsValidTargetWindowCached`로 포그라운드 창 검사 결과 캐싱 적용.
- **`Alt+Q` 전역 핫키(`RegisterHotKey` / `HwndHook`) 등록 누락 보완 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - `ChatHelper.log`에서 `RegisterGlobalHotkeys` 로그가 누락되던 현상에 대응해 `EnsureGlobalHotkeyHookRegistered()`가 `WindowInteropHelper(this).Handle` 기반 `HwndSource.FromHwnd`를 우선 사용하고, `Window_Loaded`에서도 미등록 시 자동 재시도하도록 보강.
- **전역 검색 팝업 기본 단축키 `Alt+S`로 변경 ([AppConstants.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Constants/AppConstants.cs), [AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs), [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml))**:
  - 일부 PC 환경에서 OS/드라이버가 `Alt+Q`를 선점·차단하는 문제를 피하기 위해 기본 전역 검색 단축키를 `Alt+S`로 변경(제어판에서 `Alt+Q` 등 다른 키로 변경 가능).
- **제어판 `전역 호출 단축키`(`GlobalHotkey`) 실제 연동 ([HotkeyParser.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/HotkeyParser.cs), [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs), [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs))**:
  - 기존에는 제어판에서 전역 호출 단축키를 변경해도 코드 내부가 `Alt+Q`(`VK_Q`)로 고정되어 있던 문제를 수정해, 제어판에서 `Alt+S`·`Ctrl+Space`·`F9` 등으로 변경·저장 즉시 OS 핫키와 저수준 키훅에 반영되도록 개선 (기본값 `Alt+Q` 유지).
- **재발 방지 자동 회귀 테스트 추가 ([KeyboardHookAndHotkeyTests.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper.Tests/KeyboardHookAndHotkeyTests.cs))**:
  - 일반 채팅 키(`Space`, `A`, `E`, `1~9`, `Tab`)가 훅 스레드 `_lock` 플러시를 유발하지 않는지, `EnsureHandle()` + `HwndSource.FromHwnd` 및 `RegisterHotKey(Alt+Q)`가 누락 없이 동작하는지 단위 테스트로 자동 검증.
- **GAS 재배포 필요 여부**: 불필요 (클라이언트 코드만 수정).

---

## [v2.4.2] — 2026-11-24 (기본 완성 키 `\ + Tab` 복원 · `Shift+Space` 단일 추천 전용 제한 · 마스터창 2/3 시트 정보 동기화 · `publish/` `.gitignore` 제외)

### ✨ Added / Changed
- **기본 완성 키 모드 `\ + Tab` 복원 & `Shift + Space` 단일 추천 전용 제한 ([AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs), [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**:
  - 기본 완성 트리거를 직관적인 **`\ + Tab`**(`SlashTab`)으로 복원하고, 멀티 알약(가로형 추천 바)과 1~9번 숫자 선택은 **`\ + Tab` & `\ + 숫자`**에서만 사용하도록 제한.
  - **`Shift + Space`** 모드는 **단일 추천(인라인 알약) 전용**으로 제한하여 제어판에서 `Shift + Space` 선택 시 가로형 멀티 바·번호 선택이 자동 비활성화되고 안내 문구가 표시되도록 개선 (`Ctrl + 숫자` 로직 제거).
- **마스터창 1/3 ↔ 2/3 시트 정보 불일치 수정 & `Alt+Q` 진단 로그 보강 ([MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs), [QuickSearchWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml.cs))**:
  - 프로필 전환 및 시트 동기화 시 `1/3` 도킹 바뿐만 아니라 `2/3 인식 현황`(`TxtDockRecognitionStatus`)의 시트명·개수도 함께 갱신되도록 수정.
  - `RegisterHotKey(Alt+Q/X)` 실패 시 로그 기록 및 `QuickSearchWindow` 열기 시 `ShowActivated = true` / `Topmost = true` 강제 적용.
- **`publish/` 폴더 `.gitignore` 등록**:
  - 배포용 단일 실행 파일(`178MB`)이 GitHub 100MB 제한에 걸리지 않도록 `publish/`를 `.gitignore`에 추가하고 Git 추적에서 제외.
- **GAS 재배포 필요 여부**: 불필요 (클라이언트 코드만 수정).

---

## [v2.4.1] — 2026-11-23 (DPI 배율 · 다중 모니터 알약 미노출, 커서 위치 수정 시 `Alt+Q` 먹통 및 키훅 안정화)

### 🐛 Fixed
- **커서 위치(제어판 알약 표시 위치) 수정 후 또는 검색창 재호출 시 `Alt+Q`가 동작하지 않던 문제 완벽 해결**:
  - **원인 1 (`_lastTargetHwnd` 자체 창 오염)**: 제어판(`ControlPanelWindow`)이나 빠른 검색창(`QuickSearchWindow`)이 열려 있을 때 키 입력이 발생하면 `_lastTargetHwnd`가 외부 채팅창이 아닌 ChatHelper 자체 창 핸들로 덮어써지고, `WinEventCallback`이 `!_hasCandidates` 상태에서 외부 창 포커스 전환을 추적하지 않아 제어판 저장 직후 `Alt+Q` 호출 및 텍스트 치환 대상 창을 잃어버리던 문제를 수정.
  - **원인 2 (`_hasCandidates` 미초기화로 인한 키 가로채기)**: `Alt+Q` 오픈 시 `_suggestionBar.Hide()`만 호출하고 `_hookService.ClearAll()`을 호출하지 않아, 알약이 떠 있던 상태에서 `Alt+Q`를 누르면 백그라운드 훅이 검색창의 `Tab`·숫자키를 가로채던 문제를 수정.
  - **원인 3 (`Alt+Q`/`Alt+X` 대상 창 Alt 메뉴바 활성화)**: `Alt+T`에만 적용되어 있던 `VK_NONAME(0xFC)` 더미 키 전송을 `Alt+Q`와 `Alt+X`에도 동일하게 적용하여 대상 앱(메모장·크롬 등)의 Alt 메뉴바가 포커스를 빼앗지 않도록 수정.
  - **원인 4 (`QuickSearchWindow` 커서 위치 설정 미반영 & 한글 IME 키 무시)**: `QuickSearchWindow.ShowAtCursor`가 제어판의 `PillPlacement`(`CursorNearby` / `MousePointer` / `ScreenBottomRight`)와 `PillOffsetY` 및 `fallbackTargetHwnd`를 그대로 반영하도록 통일하고, 한글 검색어 조합 중(`Key.ImeProcessed`)에도 ` ↑`/`↓`/`Enter`/`Tab`이 즉시 동작하도록 수정.
- **PC 환경별(고배율 DPI 125~175%, 다중 모니터) 알약메모 화면 이탈/미노출 현상 해결**:
  - `Win32Api.TryGetValidInputPosition`의 물리 픽셀 좌표를 WPF `SystemParameters.PrimaryScreenWidth/Height`(주 모니터 논리 DIP)에 그대로 비교·클램핑해 노트북 고배율(125%/150%)이나 보조 모니터에서 알약(`SuggestionBar`)과 빠른 검색창(`QuickSearchWindow`)이 화면 밖으로 밀려나 보이지 않던 문제를 해결.
  - [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs)에 `ConvertPhysicalPointAndGetWorkArea`(`MonitorFromPoint` + `GetMonitorInfo` + `TransformFromDevice`)를 추가해 현재 커서가 위치한 모니터의 실제 작업 영역 기준으로 정확하게 배치되도록 수정.
- **구글 채팅/카톡 타이핑 중 백그라운드 MTA 스레드 COM 호출 제거**:
  - [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs)의 키 입력 처리 루프에서 매 타건마다 크로스-프로세스 MSAA COM(`AccessibleObjectFromWindow`)이 포함됐던 `TryGetValidInputPosition` 대신 경량 `Win32Api.IsValidTargetWindow`만 호출하도록 분리하여 타이핑 지연 및 COM 실패 가능성을 제거.
  - `VK_PROCESSKEY(0xE5)` 복원 시 드라이버별 상위 비트 간섭을 막기 위해 `scanCode & 0xFF` 마스킹(`MAPVK_VSC_TO_VK`) 우선 적용.
- **GAS 재배포 필요 여부**: 불필요 (클라이언트 코드만 수정).

---

## [v2.4.0] — 2026-11-22 (현대적 UI/UX 전면 개편 — Fluent 라이트 & 미니멀 HUD 도킹 바)

### ✨ Added / Changed
- **환경설정 3탭 통합(상단 계정/동기화 바) · 디자인 실시간 미리보기 · 마스터창 `▲/▼` 전환 바 · `Shift+Space` 기본화**:
  - **단축키 충돌 검사 고도화 ([HotkeyValidator.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/HotkeyValidator.cs))**: 조합키 형식 검사, 내부 단축키(`Alt+X`, `Alt+T`) 중복 검사 및 Win32 `RegisterHotKey` 실시간 선점 검사 추가.
  - **완성 트리거 기본값 `Shift + Space` 전환 ([AppConfig.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Models/AppConfig.cs))**: 가장 안정적인 `Shift + Space`를 기본 완성 키로 설정.
  - **환경설정(`1320 × 840`) 상단 통합 헤더 & 3탭 개편 ([ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml))**:
    - 상단에 `로그인 계정` · **`종료 시 로그아웃` 체크박스** · `시트 자동 갱신 주기` · `지금 동기화`를 고정 배치하고 기존 `동기화 & 계정` 탭을 통합 제거.
    - `1~9 숫자 선택` 옵션을 `🎨 디자인 & 표시 > 추천 알약 모양` 카드로 통합하고, `사칙연산/영문번역` 토글을 `⌨️ 단축키 & 기능` 탭으로 이동.
    - `🎨 디자인 & 표시`에서 포인트 컬러에 실제 컬러 원형 스와치(`Ellipse`)를 표시하고, 우측에 **실시간 예시 화면(Live Preview)** 패널을 추가.
  - **Win32 `LWA_ALPHA` 투명도 즉시 반영 수정 ([Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs))**: `EnsureHandle()` 및 `Show()` 이후 `SetWindowAlpha` 호출, `WS_EX_LAYERED` 상시 유지(`alpha=64..255`)로 마스터창·알약창 투명도가 즉시 반영되도록 수정.
  - **특수기호(`Alt+X`) `▲ 앞으로` / `▼ 뒤로` 순서 이동 버튼 추가 ([SymbolPickerWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SymbolPickerWindow.xaml))**.
  - **마스터창(`MainWindow`) `▲ / ▼` 수동 전환형 1줄 바 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml))**: 사용자가 `▲ / ▼`를 누를 때만 `1/3 메뉴 & 시트` ↔ `2/3 실시간 인식 현황` ↔ `3/3 최근 로그` 화면으로 전환.
  - **종료 확인 창 간결화 ([ExitConfirmWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ExitConfirmWindow.xaml))**: `취소` / `종료` 2개 버튼만 남기고 로그아웃 여부는 환경설정 상단 체크박스에 따르도록 개선.
- **제어판 통합 개편 (`커맨드 & 시트 관리` 2단 분할) · 포인트 컬러 선택 · Win32 진짜 반투명 · 중앙 하단 도킹 바**:
  - **커맨드 & 시트 관리 통합**: `데이터 소스` 탭을 `커맨드 관리` 상단 1줄(`업무 프로필` + 접이식 `개인 시트 추가/삭제`)로 통합하고, 창 크기를 `1160 × 760`(화면 비율 자동 보정)으로 넓혀 **좌측 목록 ↔ 우측 긴 본문 전용 편집기(`GridSplitter` + 자동 스크롤 + 글자 수 표시)** 2단 레이아웃으로 개편.
  - **동기화 실시간 반영**: [TemplateManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TemplateManager.cs) `TemplatesSynced` 이벤트를 통해 동기화 중/완료 직후 제어판 커맨드 목록이 자동 갱신됨.
  - **Windows 11 Fluent 라이트 + 포인트 컬러 선택**: 다크 모드 대신 깔끔한 화이트 베이스 위에 **블루(기본) / 민트 그린 / 라벤더 / 코랄 피치 / 로즈 핑크** 포인트 컬러를 실시간 선택 가능([ThemeManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ThemeManager.cs)).
  - **마우스 렉 없는 Win32 진짜 반투명 (`LWA_ALPHA`)**: `AllowsTransparency="False"` 상태에서 화면만 어두워지던 `Window.Opacity`를 [Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs) `SetWindowAlpha`(`SetLayeredWindowAttributes`)로 교체해 실제 뒤 창이 비치도록 수정.
  - **특수기호(`Alt+X`) 입력창 확대 & 마스터창 중앙 하단 배치**: `TxtNewSymbol` 높이 `36px`/`15pt`로 확대, [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs) 도킹 바를 메인 모니터 중앙 하단으로 배치.
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
- **빌드 출력 경로를 C# 표준 `publish/` 단일 경로로 통일 및 에이전트 빌드 매뉴얼 명문화 ([AGENTS.md](file:///C:/Users/taerin/Desktop/채팅도우미/AGENTS.md), [README.md](file:///C:/Users/taerin/Desktop/채팅도우미/README.md))**:
  - 과거 Python(PyInstaller) 관례였던 `dist/` 중복 빌드를 제거하고, C# .NET 표준인 **`publish/`** 단일 폴더(`dotnet publish ChatHelper\ChatHelper.csproj -c Release -o publish`)로 통일하여 다른 컴퓨터에서 `git pull` 후에도 혼선 없이 동일하게 빌드·실행할 수 있도록 [AGENTS.md](file:///C:/Users/taerin/Desktop/채팅도우미/AGENTS.md)에 매뉴얼을 추가.
- **실시간 예시 미리보기 위치 이동 반영 · `Alt+Q` 빠른 검색창 화면 정중앙 배치 · 추천 알약 및 번역기 우측 상단 `✕` 즉시 닫기 버튼 추가**:
  - **실시간 예시 미리보기 알약 위치 동적 반영 ([ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs))**: `🎨 디자인 & 표시`에서 `채팅 커서 밀착 (+15px)` / `넉넉한 간격 (+35px)` / `원거리 (+65px)` / `마우스 포인터 위치` / `모니터 우측 하단 고정`을 선택할 때마다 미리보기 캔버스 안의 알약(`PreviewPillBorder`)이 실제로 해당 간격·마우스 포인터(`🖱️ 마우스 포인터 위치` 배지 표시)·우측 하단으로 이동하도록 개선.
  - **`Alt+Q` 빠른 검색창(`QuickSearchWindow`) 화면 중앙 배치 ([QuickSearchWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml.cs))**: 특수기호 창(`Alt+X`)과 동일하게 항상 메인 작업 영역(`SystemParameters.WorkArea`)의 정중앙에 열리도록 수정하여 좌측이나 마스터창 기준으로 치우쳐 열리던 문제를 해결.
  - **추천 알약 및 번역기 카드 우측 상단 `✕` 닫기 버튼 추가 ([SuggestionBar.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml), [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs))**: 자동 숨김 카운트다운 시간이 남아 있더라도 언제든 추천 알약(`RecognitionHeaderBorder`), 번역 로딩창(`ShowAiGeneratingPlaceholder`), 번역 결과 비교 카드(`RenderTranslationRow`)의 우측 상단 `✕` 버튼을 눌러 즉시 닫고 입력 버퍼를 정리(`DismissSuggestion`)할 수 있도록 개선.
- **마스터창 `▲/▼` 좌측 끝 배치 · 인식 배지/알약 색상 구분 · 본문 줄 간격 확대 · 안내 문구 정리 · 불투명도(`WM_STYLECHANGING`) 완벽 적용 · 미리보기 배경 문구 가시성 개선**:
  - **마스터창 `▲ 1/3 ▼` 화살표 좌측 끝 이동 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml))**: 화면 전환 화살표(`▲ 1/3 ▼`)를 도킹 바 가장 왼쪽 끝(`Grid.Column="0"`)에 배치하여 어느 화면에서든 일정한 위치에서 즉시 클릭할 수 있도록 개선.
  - **인식 알약 및 `인식 현황`/`최근 로그` 배지 색상 구분 ([MainWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml), [ModernLightTheme.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Themes/ModernLightTheme.xaml))**:
    - `인식 현황` 및 `최근 로그` 배지에 포인트 파스텔 배경(`ThemeAccent`) + 포인트 테두리(`ThemeAccentBorder`) + 짙은 포인트 텍스트(`ThemeAccentText`)를 적용해 흰색 도킹 바 위에서 명확히 구분되도록 개선.
    - 추천 알약(`SuggestionBar`) 배경(`ThemePillBg`)을 순백색(`#FFFFFF`) 대신 쿨 슬레이트 틴트(`#F1F5F9`)와 선명한 테두리(`#94A3B8`)로 구분하여 흰색 채팅창 위에서도 경계와 추천 칩(`#FFFFFF` 및 포인트 칩)이 뚜렷하게 보이도록 개선.
  - **커맨드 목록 및 본문 줄 간격(LineHeight) 확대 ([ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml), [QuickSearchWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml))**:
    - 제어판 커맨드 목록(`DgTemplates`)의 고정 `RowHeight="30"` 때문에 2줄 본문 아랫줄이 반쯤 잘리던 문제를 해결하기 위해 `MinRowHeight="46"` 및 `내용 미리보기` 컬럼 `LineHeight="19"`(`MaxHeight="38"`)를 적용.
    - 우측 본문 편집기(`TxtContent`)에 `LineHeight="22"`, 빠른 검색창(`QuickSearchWindow`) 본문 미리보기에 `LineHeight="19"`를 적용해 긴 본문 가독성을 개선.
  - **환경설정 하단 어색한 안내 문구 수정 ([ControlPanelWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml.cs))**: 구버전 레이아웃 기준 문구(`"하단 입력란에서 수정 후 [✏️ 수정 적용 (시트 반영)]을 누르세요."`)를 현재 2단 레이아웃에 맞게 `"우측 상세 편집기에서 내용을 수정한 뒤 [✏️ 수정 적용]을 누르세요."`로 수정.
  - **실제 창 불투명도(`SetWindowAlpha`) 미적용 버그 근본 수정 ([Win32Api.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/Win32Api.cs))**:
    - `AllowsTransparency="False"`인 WPF 창에 `SetWindowLong(GWL_EXSTYLE, WS_EX_LAYERED)` 호출 시 WPF 내부 `Window.WmStyleChanging`(`WM_STYLECHANGING 0x007C`)이 `WS_EX_LAYERED` 플래그를 강제로 다시 지워버리던 원인을 규명하고, `HwndSourceHook`으로 `WM_STYLECHANGING`에서 `WS_EX_LAYERED` 비트를 보존하도록 수정하여 미리보기와 동일하게 마스터창·알약창·검색창 반투명도가 실제 적용되도록 해결.
  - **실시간 예시 미리보기 뒷부분 배경 문구 가시성 개선 ([ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml))**: 미리보기 알약 바로 뒤에 1줄만 배치되어 알약에 완전히 가려지던 배경 문구를 실제 여러 줄 상담 대화 카드로 확장하여, 알약 위·뒤·아래에 걸쳐 투명도(70%~100%)에 따른 뒤 배경 비침 효과를 한눈에 확인할 수 있도록 개선.
- **파스텔 포인트 컬러 톤다운(흰색에 가까운 부드러운 파스텔) & 마우스 오버 시 텍스트 가독성 개선**:
  - [ThemeManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/ThemeManager.cs) 및 [ModernLightTheme.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Themes/ModernLightTheme.xaml)에서 원색에 가깝던 포인트 컬러를 흰색에 가까운 은은한 파스텔톤(`#DBEAFE`, `#D1FAE5`, `#EDE9FE`, `#FFEDD5`, `#FCE7F3`)으로 톤다운하고, 마우스 오버 시 더 연한 파스텔 배경(`ThemeButtonHoverBg`) + 또렷한 짙은 포인트 텍스트(`ThemeAccentText`)가 유지되도록 개선.
  - [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs), [SymbolPickerWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SymbolPickerWindow.xaml.cs), [QuickSearchWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/QuickSearchWindow.xaml.cs), [LoginWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/LoginWindow.xaml), [ExitConfirmWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ExitConfirmWindow.xaml)의 모든 포인트 컬러 버튼/칩에 커스텀 호버 스타일 및 `ThemeAccentText`를 적용해 마우스 오버 시 글자가 안 보이던 문제를 해결.
- **알약 20초 초과 미소멸(`0s` 정지) 버그 수정 & 화면 밖 마우스 폴백/위치 보정 안내 추가**:
  - [SuggestionBar.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml.cs#L120-L133)의 카운트다운 타이머가 `0s`에 도달하면 일반·미일치 알약을 즉시 `Hide()`하고 [MainWindow.xaml.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/MainWindow.xaml.cs)에서 `_hookService.ClearAll()`을 함께 호출하도록 수정, [KeyboardHookService.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/KeyboardHookService.cs#L83-L93) `OnAutoHideTimerElapsed`의 버퍼 잔류 조건도 보완.
  - 입력 커서가 화면 밖이거나 큰 알약이 모니터 경계를 벗어나 잘릴 때 **마우스 커서 폴백 및 내측 보정**을 수행하고, 알약 하단에 일시 위치 조정 안내(`TxtPositionNotice`)를 표시([SuggestionBar.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/SuggestionBar.xaml#L41-L43)).
  - 오답노트([docs/LESSONS_LEARNED.md](file:///C:/Users/taerin/Desktop/채팅도우미/docs/LESSONS_LEARNED.md)) 섹션 10 기록 및 단위 테스트 검증 추가.
- **로컬 PC 전용 목록 단순화 (구글 시트·JSON·헤더 설정 완전 제거)**:
  - [ControlPanelWindow.xaml](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/UI/ControlPanelWindow.xaml#L436-L454) > `💻 로컬 PC 시트 (이 PC 전용)`에서 구글 시트 URL·탭 이름·헤더·`config.json` 안내를 모두 제거하고, **목록 이름만 입력**하면 즉시 생성·적용되어 **`📋 커맨드 관리` 탭에서 바로 단축어·답변을 추가/수정/삭제**할 수 있도록 개선([TemplateManager.cs](file:///C:/Users/taerin/Desktop/채팅도우미/ChatHelper/Services/TemplateManager.cs#L147-L190)).
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
