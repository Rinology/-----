from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QRadioButton, QButtonGroup,
    QLabel, QLineEdit, QPushButton, QFrame, QMessageBox, QGroupBox
)
from template_manager import TemplateManager


class SettingsDialog(QDialog):
    settings_saved = pyqtSignal()

    def __init__(self, manager: TemplateManager, parent=None):
        super().__init__(parent)
        self.manager = manager
        self.setWindowTitle("채팅 도우미 설정 (자동완성 모드 선택)")
        self.setFixedSize(480, 520)
        self.setStyleSheet("""
            QDialog {
                background-color: #18181b;
                color: #f4f4f5;
            }
            QLabel {
                color: #e4e4e7;
                font-size: 13px;
            }
            QGroupBox {
                border: 1px solid #3f3f46;
                border-radius: 8px;
                margin-top: 12px;
                padding-top: 14px;
                font-weight: bold;
                color: #60a5fa;
            }
            QGroupBox::title {
                subcontrol-origin: margin;
                left: 10px;
                padding: 0 4px;
            }
            QLineEdit {
                background-color: #27272a;
                color: #f4f4f5;
                border: 1px solid #52525b;
                border-radius: 6px;
                padding: 6px 10px;
                font-size: 13px;
            }
            QLineEdit:focus {
                border: 1.5px solid #3b82f6;
            }
            QRadioButton {
                color: #f4f4f5;
                font-size: 13px;
                spacing: 8px;
                padding: 4px 0;
            }
            QRadioButton::indicator {
                width: 16px;
                height: 16px;
            }
            QPushButton {
                background-color: #2563eb;
                color: #ffffff;
                border: none;
                border-radius: 6px;
                padding: 8px 16px;
                font-size: 13px;
                font-weight: 600;
            }
            QPushButton:hover {
                background-color: #1d4ed8;
            }
            QPushButton#CancelBtn {
                background-color: #3f3f46;
                color: #e4e4e7;
            }
            QPushButton#CancelBtn:hover {
                background-color: #52525b;
            }
        """)

        self.init_ui()

    def init_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 16, 18, 16)
        layout.setSpacing(14)

        # 1. 자동완성 모드 선택 그룹
        mode_group = QGroupBox("📌 커서 추적 자동완성 모드 선택")
        mode_layout = QVBoxLayout(mode_group)
        mode_layout.setSpacing(8)

        self.radio_group = QButtonGroup(self)

        # 옵션 1
        self.radio_inline = QRadioButton("옵션 1: 스마트폰 인라인 툴팁 (추천)")
        self.desc_inline = QLabel("  • 글을 칠 때 커서 옆에 [Tab] 알약 칩 1개가 작게 따라다님\n  • 마음에 들면 Tab 키로 쏙 완성!")
        self.desc_inline.setStyleSheet("color: #a1a1aa; font-size: 11px;")
        mode_layout.addWidget(self.radio_inline)
        mode_layout.addWidget(self.desc_inline)

        # 옵션 2
        self.radio_horizontal = QRadioButton("옵션 2: 가로형 미니 추천 바")
        self.desc_horizontal = QLabel("  • 커서 위에 2~3개의 추천 후보가 가로로 나열됨\n  • Tab(1번) 또는 숫자키(1, 2, 3), 마우스 클릭으로 선택")
        self.desc_horizontal.setStyleSheet("color: #a1a1aa; font-size: 11px;")
        mode_layout.addWidget(self.radio_horizontal)
        mode_layout.addWidget(self.desc_horizontal)

        # 옵션 3
        self.radio_manual = QRadioButton("옵션 3: 수동 단축키 모드 (Alt+Q만 사용)")
        self.desc_manual = QLabel("  • 타이핑 중에는 조용히 숨어있고, 필요할 때만 Alt+Q로 호출")
        self.desc_manual.setStyleSheet("color: #a1a1aa; font-size: 11px;")
        mode_layout.addWidget(self.radio_manual)
        mode_layout.addWidget(self.desc_manual)

        self.radio_group.addButton(self.radio_inline, 1)
        self.radio_group.addButton(self.radio_horizontal, 2)
        self.radio_group.addButton(self.radio_manual, 3)

        layout.addWidget(mode_group)

        # 2. 단축키 및 단축어 설정 그룹
        shortcut_group = QGroupBox("⌨️ 단축키 및 단축어 설정")
        sc_layout = QVBoxLayout(shortcut_group)
        sc_layout.setSpacing(8)

        row1 = QHBoxLayout()
        row1.addWidget(QLabel("수동 팝업 단축키:"))
        self.hotkey_input = QLineEdit()
        row1.addWidget(self.hotkey_input)
        sc_layout.addLayout(row1)

        row2 = QHBoxLayout()
        row2.addWidget(QLabel("단축어 트리거 접두사:"))
        self.prefix_input = QLineEdit()
        self.prefix_input.setPlaceholderText("기본값: \\ (예: \\ㅇㄴ, \\계좌)")
        row2.addWidget(self.prefix_input)
        sc_layout.addLayout(row2)

        layout.addWidget(shortcut_group)

        # 3. 구글 스프레드시트 연동
        remote_group = QGroupBox("📊 구글 스프레드시트 CSV 연동")
        rm_layout = QVBoxLayout(remote_group)
        rm_layout.setSpacing(6)

        self.url_input = QLineEdit()
        self.url_input.setPlaceholderText("구글 시트 '웹에 게시' CSV URL을 붙여넣으세요")
        rm_layout.addWidget(self.url_input)

        sync_btn = QPushButton("🔄 지금 시트에서 동기화 테스트")
        sync_btn.setStyleSheet("background-color: #27272a; border: 1px solid #3f3f46; font-size: 12px; font-weight: normal;")
        sync_btn.clicked.connect(self.test_sync)
        rm_layout.addWidget(sync_btn)

        layout.addWidget(remote_group)

        # 4. 하단 버튼
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()

        cancel_btn = QPushButton("취소")
        cancel_btn.setObjectName("CancelBtn")
        cancel_btn.clicked.connect(self.reject)
        btn_layout.addWidget(cancel_btn)

        save_btn = QPushButton("저장 및 적용")
        save_btn.clicked.connect(self.save_settings)
        btn_layout.addWidget(save_btn)

        layout.addLayout(btn_layout)

        # 기존 설정값 로드
        self.load_values()

    def load_values(self):
        cfg = self.manager.config
        mode = cfg.get("floating_bar_mode", "inline_pill")
        if mode == "horizontal_bar":
            self.radio_horizontal.setChecked(True)
        elif mode == "manual_only":
            self.radio_manual.setChecked(True)
        else:
            self.radio_inline.setChecked(True)

        self.hotkey_input.setText(cfg.get("hotkey", "<alt>+q"))
        self.prefix_input.setText(cfg.get("expansion_prefix", "\\"))
        self.url_input.setText(cfg.get("remote_csv_url", ""))

    def save_settings(self):
        cfg = self.manager.config
        if self.radio_horizontal.isChecked():
            cfg["floating_bar_mode"] = "horizontal_bar"
        elif self.radio_manual.isChecked():
            cfg["floating_bar_mode"] = "manual_only"
        else:
            cfg["floating_bar_mode"] = "inline_pill"

        cfg["hotkey"] = self.hotkey_input.text().strip() or "<alt>+q"
        cfg["expansion_prefix"] = self.prefix_input.text()
        cfg["remote_csv_url"] = self.url_input.text().strip()

        self.manager.save_config()
        self.settings_saved.emit()
        self.accept()

    def test_sync(self):
        url = self.url_input.text().strip()
        if not url:
            QMessageBox.warning(self, "경고", "먼저 구글 시트 CSV URL을 입력해 주세요.")
            return

        self.manager.config["remote_csv_url"] = url
        success, msg = self.manager.sync_from_remote_csv()
        if success:
            QMessageBox.information(self, "성공", msg)
        else:
            QMessageBox.critical(self, "오류", msg)
