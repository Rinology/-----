# 💻 ChatHelper v2.0 (Windows 98 Millennium Edition)

파이썬의 타이핑 지연을 완전히 극복한 **초경량 C# .NET 8 네이티브 · Zero-Lag(0ms) · 스마트폰형 커서 추적 · 로컬 AI 탑재 채팅 도우미**입니다.

---

## 🌟 주요 기능

1. **🤖 100% 로컬 AI 상담 비서 (Alt + X)**:
   - **화면 캡처 자동 OCR**: 상대방 대화 말풍선이 여러 개일 때, `Win + Shift + S`로 화면 영역을 캡처하고 `Alt + X`만 누르면 **Windows 내장 OCR(0.05초)**이 글자를 자동 인식합니다.
   - **복사 텍스트 지원**: 텍스트를 드래그해서 `Ctrl + C` 복사 후 `Alt + X`를 눌러도 즉시 분석합니다.
   - **정중한 CS 한국어 답변**: 초경량 AI(Qwen 2.5 1.5B)가 사족 없이 바로 전송 가능한 친절한 상담원 문장을 1초 만에 완성합니다.
   - **초고속 타이핑 주입**: 완성된 답변을 보고 **`[Tab]`** 키만 누르면 고객 채팅창에 0.01초 만에 자동 타이핑됩니다.
   - **100% 로컬 & 보안 보장**: 인터넷이 끊겨도 작동하며 고객 상담 대화가 외부로 1바이트도 유출되지 않습니다.
   - **규칙 커스텀**: [Prompts/ai_system_rules.json](file:///c:/Users/taerin/Desktop/채팅도우미/-----/ChatHelper/Prompts/ai_system_rules.json)에서 말투와 규칙을 언제든 자유롭게 수정 가능합니다.

2. **화면 상태표시창 & 작업표시줄 실행 상태 표시**:
   - 실행 시 화면 우측 하단 및 작업표시줄에 **`🟢 ChatHelper v2.0 ME - 실행 중`** 상태바가 표시됩니다.
   - **[💻 제어판]**, **[🔍 검색 (Alt+Q)]**, **[🤖 AI비서 (Alt+X)]**, **[⏸️ 일시정지]**, **[🔽 트레이 이동]**을 클릭 한 번으로 조작할 수 있습니다.

3. **비입력 화면 오작동 완전 차단 (스마트 필터링)**:
   - `Ctrl+C`, `Ctrl+V`, `Ctrl+A` 등 단축키 입력 시 추천 알약이 뜨지 않도록 필터링합니다.
   - 타이핑이 멈추면 **3.5초 뒤 자동으로 사라집니다.**

4. **자연스러운 스마트폰 스타일 단축어 자동완성**:
   - 평소처럼 한글 모드 그대로 **`ㅇㄴ`** (물리키 `ds`), **`ㅂㅅ`** (물리키 `qt`) 등을 치면 커서 옆에 추천 알약이 뜹니다.
   - 마음에 들면 **`\ + Tab`**으로 0.01초 만에 문장을 완성합니다.

5. **윈도우 98 레트로 제어판 & 스프레드시트 연동**:
   - **커맨드 관리**: 실전 CS 템플릿 추가/수정/삭제
   - **구글 스프레드시트 연동**: 온라인 원격 동기화 지원

---

## 💻 다른 컴퓨터에서 처음 세팅할 때 (개발 및 작업 환경)

다른 컴퓨터에서 깃허브 코드를 내려받은 후 아래 2가지만 설치해 주시면 바로 작동합니다.

### 1단계. .NET 8.0 런타임/SDK 설치
* **실행용 데스크톱 런타임 다운로드**: [.NET Desktop Runtime 8.0 (Windows x64)](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.13-windows-x64-installer)
* **개발/빌드용 SDK 다운로드**: [.NET 8.0 SDK (Windows x64)](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-8.0.406-windows-x64-installer)
* 또는 파워셸(관리자)에서 명령어로 설치:
  ```powershell
  winget install Microsoft.DotNet.SDK.8
  ```

### 2단계. 로컬 AI 엔진 (Ollama) 및 모델 다운로드
1. **Ollama 공식 사이트 접속 후 설치**:  
   👉 [https://ollama.com/download/windows](https://ollama.com/download/windows) 에 접속하여 설치 파일을 받아 실행합니다.  
   *(또는 파워셸에서 `irm https://ollama.com/install.ps1 | iex` 실행)*

2. **터미널에서 한국어 경량 모델 1회 다운로드**:  
   명령 프롬프트(CMD) 또는 파워셸을 열고 아래 명령어를 1회 실행합니다:
   ```cmd
   ollama run qwen2.5:1.5b
   ```
   *(용량 약 980MB, 다운로드가 끝나고 `>>>` 대화창이 뜨면 `/bye`를 입력해 종료하시면 됩니다. 컴퓨터에 한 번만 받아두면 이후에는 다시 받을 필요 없이 0.5초 만에 실행됩니다!)*

---

## 🚀 실행 방법

* [run.bat](file:///c:/Users/taerin/Desktop/채팅도우미/-----/run.bat) 더블클릭 (또는 [publish/ChatHelper.exe](file:///c:/Users/taerin/Desktop/채팅도우미/-----/publish/ChatHelper.exe) 직접 실행)
