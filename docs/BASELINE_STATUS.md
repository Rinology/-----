# ChatHelper 현재 상태 기준선 (Baseline) — 다음 작업 시 참고용

> 작성: 2026-11-21 / 버전 v2.2.0 + 데이터 소스 프로필 기능
> 이 문서는 "지금 정상 동작하는 기능 목록"이다. 이후 수정 시 아래 항목이 깨지지 않았는지 회귀 점검할 것.

## 1. 정상 동작 확인된 기능 (회귀 체크리스트)

| 영역 | 기능 | 확인 방법 |
|---|---|---|
| 자동완성 | `\키워드` + Tab 치환, 초성 검색(`ㅎㅂ`→환불), 번호 선택 | 채팅창에서 `\ed` 등 입력 |
| 한글 입력 인식 | `HangulImeBuffer`: 조합 중 Backspace=자모 1개, 확정 음절 Backspace=음절 1개 | 단위테스트 `TemplateManagerTests`, `"가로형 "`→BS×2→`"형"` = `"가로형"` |
| 인식 배너 | `🔎 인식됨`, `⏳ [대기중]`, `❓ [알아듣지 못함]` | 상태창 확인 |
| 로컬 AI | Ollama `qwen2.5:3b`, 실시간 AI 추천(`[\+A]`), 영문 번역(`[\+E]`), 용어집 RAG(별칭→공식 용어 치환) | AI: ON 상태에서 입력 |
| 계산 | `5 * 10 =` → `50` (`MathCalcHelper`), ON/OFF 가능 | 단위테스트 |
| 번역 | `Alt+T` 드래그 양방향 번역(`Auto` / `KoToEn` / `EnToKo`), `TranslationSession` 역번역 & 클립보드 시퀀스 검증 | 제어판 & `TemplateManagerTests` |
| 상담 메모 | `챗헬프 임시메모`(Alt+N, 의미 이름표·줄나누기) ↔ `Xenie`(\+N, 위치유지·원클릭 변수 채우기) 자석 도킹 & Topmost | 마스터창 & `TemplateManagerTests` |
| OCR | `Win+Shift+S` 캡처 OCR (`RapidOcrNet` PP-OCRv5 한국어 ONNX 1순위 + Windows 내장 OCR 폴백), Zero-Alpha 복구, 다크배경 반전 | `AiAndOcrIntegrationTests` & `TemplateManagerTests` |
| 배포 보안 | `%LocalAppData%\ChatHelper\` 설정/7일 로그 분리, DPAPI 아이디 암호화, `publish/` 민감파일 자동 정리 | `ConfigManagerTests` |
| 시트 동기화 | 채팅템플릿/용어집/톤앤매너 CSV 읽기, 15분 주기 자동, 수동 즉시, 2회 재시도, 오프라인 로컬 캐시 | 제어판 > 동기화 |
| 시트 쓰기 | 키워드/용어 등록·삭제 → GAS `doPost` | 용어집 창, 등록 기능 |
| 특수기호 바 | `Alt+S`(기본) 상단 1클릭 기호(→ ■ ★ ✔ 등) | 빠른검색창 |
| OCR | `Win+Shift+S` 캡처 OCR, Zero-Alpha 복구 | `AiAndOcrIntegrationTests` |
| 텍스트 주입 | `SendInput` 원자 입력, `Shift+Home`/방향키 확장키 처리 | 치환 동작 |
| 로그인 | USER_SHEET_URL 기반 계정, 로그인 유지 | 시작 시 |

단위/통합 테스트: `dotnet run --no-restore --project ChatHelper.Tests\ChatHelper.Tests.csproj` → 전부 `[PASS]` 여야 함.

## 2. 의도적으로 유지하는 정책 (바꾸지 말 것)
- 로컬 Ollama 구조 유지(`qwen2.5:3b`).
- 한→영 번역 결과는 **구글 시트에 업로드하지 않음**, **톤앤매너 적용하지 않음**.
- 로그인(USER_SHEET_URL)은 프로필 대상이 아님(.env 고정).

## 3. 데이터 소스 프로필 (읽기·쓰기 모두 해당 시트)

### 개념 (방법 B: GAS 중앙 관리)
- `.env`에는 `USER_SHEET_URL`, `LOCAL_LLM_ENDPOINT`, `SHEET_WRITE_WEBHOOK_URL` **3줄만** 유지.
- `GAS/Code.gs` (v3) 상단 `PROFILES` 배열에 시트 이름/URL을 등록하면, 앱이 `doGet?action=profiles`로 목록을 받아 제어판 드롭다운에 표시.
- 읽기(`doGet?action=read`)와 쓰기(`doPost`) 모두 GAS가 처리하므로 시트를 '웹에 게시'하거나 '링크 공유'로 풀지 않아도 동작 (단, GAS 배포 계정이 시트 편집 권한 보유 필요).
- GAS v3 미배포/오프라인 시에는 `AppConstants` 기본 게시 URL 및 로컬 캐시로 fallback.
- GAS 웹앱 URL 은 모든 프로필 공용(`.env SHEET_WRITE_WEBHOOK_URL`).
- 읽기는 `.../gviz/tq?tqx=out:csv&sheet=<탭이름>` 방식 → 시트를 '링크가 있는 모든 사용자 - 뷰어' 이상으로 공유해야 함.
- 쓰기는 GAS 가 해당 시트를 열어야 하므로 GAS 계정이 편집 권한을 가져야 하고, 시트 ID를 GAS 속성 `ALLOWED_SPREADSHEET_IDS` 에 등록해야 함.

### 관련 파일
| 파일 | 역할 |
|---|---|
| `ChatHelper/Models/DataSourceProfile.cs` | 프로필 모델(이름, 스프레드시트 URL, 탭 3개) |
| `ChatHelper/Models/AppConfig.cs` | `DataSourceProfiles`, `ActiveDataSourceProfile` (config.json) |
| `ChatHelper/Services/DataSourceResolver.cs` | 읽기 URL/쓰기 대상 결정, `ProfileChanged` 이벤트 |
| `ChatHelper/UI/ControlPanelWindow.DataSources.cs` + xaml `🗂 데이터 소스` 탭 | 프로필 UI |
| `ChatHelper/MainWindow.xaml.cs` | `ProfileChanged` → 3종 즉시 재동기화 |
| `GAS/Code.gs` (v2) | 미들웨어: `spreadsheetUrl`/`target`/`sheetName` 라우팅 |

### GAS 요청 규약 (앱 → GAS)
`{ "target": "template|glossary", "sheetName": "", "spreadsheetUrl": "", "action": "append|delete", ... }`
- 기본 프로필이면 `spreadsheetUrl` 이 비어 GAS 속성 `SHEET_URL` 사용. 구버전 요청과도 호환.

### 알려진 제약 / 다음 작업 후보
- 로컬 캐시 파일(`chat_templates.csv`, `glossary_cache.csv`)은 프로필별로 분리돼 있지 않음 → 전환 직후 오프라인이면 이전 프로필 캐시가 보일 수 있음. (개선안: 프로필별 캐시)
- 새 프로필의 시트 컬럼 구조는 기본 시트와 동일해야 함.
## 4. 빌드/배포 메모
- NuGet 복원은 최초 1회 `dotnet restore`(샌드박스 밖), 이후 `--no-restore`.
- 배포: `dotnet publish ChatHelper\ChatHelper.csproj -c Release --no-restore -o publish` (실행 중이면 파일 잠김 → 앱 종료 후).
- GAS 수정 시 반드시 **새 배포** 후 웹앱 URL(.env `SHEET_WRITE_WEBHOOK_URL`)을 확인.
