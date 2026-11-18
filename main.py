import os
import sys
import threading
import time
from PyQt6.QtCore import QObject, pyqtSignal, QTimer, Qt
from PyQt6.QtWidgets import (
    QApplication, QSystemTrayIcon, QMenu, QMessageBox
)
from PyQt6.QtGui import QIcon, QPixmap, QPainter, QColor, QFont
from pynput import keyboard

from template_manager import TemplateManager
from overlay_ui import OverlayWindow
from text_expander import TextExpander
import windows_helper


class HotkeyBridge(QObject):
    """pynput 스레드에서 PyQt 메인 GUI 스레드로 신호를 전달하는 브리지"""
    open_overlay_signal = pyqtSignal(int)
    show_tray_message_signal = pyqtSignal(str, str)


def create_tray_icon() -> QIcon:
    """트레이 아이콘 동적 생성 (별도 이미지 파일 없이 렌더링)"""
    pixmap = QPixmap(32, 32)
    pixmap.fill(Qt.GlobalColor.transparent)

    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)

    # 둥근 파란색 배경
    painter.setBrush(QColor("#2563eb"))
    painter.setPen(Qt.PenStyle.NoPen)
    painter.drawRoundedRect(2, 2, 28, 28, 7, 7)

    # 말풍선 느낌의 'C' 글자
    painter.setPen(QColor("#ffffff"))
    font = QFont("Malgun Gothic", 15, QFont.Weight.Bold)
    painter.setFont(font)
    painter.drawText(pixmap.rect(), Qt.AlignmentFlag.AlignCenter, "C")
    painter.end()

    return QIcon(pixmap)


class ChatHelperApp:
    def __init__(self):
        self.app = QApplication(sys.argv)
        self.app.setQuitOnLastWindowClosed(False)

        # 1. 템플릿 관리자
        self.manager = TemplateManager("config.json")

        # 2. 브리지 및 오버레이 윈도우 (커서 추적 팝업)
        self.bridge = HotkeyBridge()
        self.bridge.open_overlay_signal.connect(self.on_hotkey_triggered)
        self.bridge.show_tray_message_signal.connect(self.on_show_tray_message)

        self.overlay = OverlayWindow(self.manager)
        self.overlay.request_paste.connect(self.handle_paste_request)

        # 3. 텍스트 익스팬더 (단축어 자동 대치 엔진)
        self.expander = TextExpander(
            self.manager,
            on_expanded_callback=self.on_text_expanded
        )
        self.expander.start()

        # 4. 트레이 아이콘 설정
        self.tray_icon = QSystemTrayIcon(create_tray_icon(), self.app)
        self.setup_tray_menu()
        self.tray_icon.show()

        # 5. 글로벌 핫키 리스너 시작
        self.hotkey_listener = None
        self.start_hotkey_listener()

        # 시작 알림
        hotkey_name = self.manager.config.get("hotkey", "<alt>+q")
        prefix = self.manager.config.get("expansion_prefix", "\\")
        self.tray_icon.showMessage(
            "채팅 도우미 시작됨 (커서 추적 모드)",
            f"1. 커서 옆 추천 창: [{hotkey_name}]\n2. 단축어 자동 치환: [{prefix}ㅇㄴ] 입력 시 즉시 완성",
            QSystemTrayIcon.MessageIcon.Information,
            3500
        )

    def setup_tray_menu(self):
        menu = QMenu()
        menu.setStyleSheet("""
            QMenu {
                background-color: #27272a;
                color: #f4f4f5;
                border: 1px solid #3f3f46;
                padding: 4px;
            }
            QMenu::item {
                padding: 6px 20px;
                border-radius: 4px;
            }
            QMenu::item:selected {
                background-color: #2563eb;
            }
            QMenu::separator {
                height: 1px;
                background: #3f3f46;
                margin: 4px 8px;
            }
        """)

        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")
        action_open = menu.addAction(f"커서 옆 추천 창 열기 ({hotkey_str})")
        action_open.triggered.connect(self.trigger_overlay_manual)

        action_reload = menu.addAction("CSV 템플릿 다시 불러오기 (Ctrl+R)")
        action_reload.triggered.connect(self.reload_csv)

        action_sync = menu.addAction("구글 시트에서 최신 동기화")
        action_sync.triggered.connect(self.sync_remote)

        menu.addSeparator()

        action_open_csv = menu.addAction("CSV 템플릿 파일 열기")
        action_open_csv.triggered.connect(self.open_csv_file)

        action_open_config = menu.addAction("설정 파일(config.json) 열기")
        action_open_config.triggered.connect(self.open_config_file)

        menu.addSeparator()

        action_quit = menu.addAction("종료")
        action_quit.triggered.connect(self.quit_app)

        self.tray_icon.setContextMenu(menu)
        self.tray_icon.setToolTip(f"채팅 도우미 (커서 추적: {hotkey_str})")
        self.tray_icon.activated.connect(self.on_tray_activated)

    def on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self.trigger_overlay_manual()

    def trigger_overlay_manual(self):
        hwnd = windows_helper.get_foreground_window()
        self.overlay.show_overlay_at_cursor(hwnd)

    def start_hotkey_listener(self):
        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")

        def on_activate():
            hwnd = windows_helper.get_foreground_window()
            self.bridge.open_overlay_signal.emit(hwnd)

        try:
            hotkey_mapping = {hotkey_str: on_activate}
            self.hotkey_listener = keyboard.GlobalHotKeys(hotkey_mapping)
            self.hotkey_listener.daemon = True
            self.hotkey_listener.start()
            print(f"[단축키 등록 완료] {hotkey_str}")
        except Exception as e:
            print(f"[단축키 등록 실패] {e}")

    def on_hotkey_triggered(self, hwnd: int):
        if self.overlay.isVisible():
            self.overlay.hide_overlay()
        else:
            self.overlay.show_overlay_at_cursor(hwnd)

    def on_text_expanded(self, title: str):
        # 텍스트 대치가 완료되었을 때 가벼운 알림 (트레이 툴팁 갱신)
        self.bridge.show_tray_message_signal.emit("단축어 자동완성", f"'{title}' 완성됨")

    def on_show_tray_message(self, title: str, msg: str):
        self.tray_icon.showMessage(title, msg, QSystemTrayIcon.MessageIcon.Information, 1000)

    def handle_paste_request(self, text: str, target_hwnd: int):
        restore_cb = self.manager.config.get("restore_clipboard", True)
        delay_ms = self.manager.config.get("paste_delay_ms", 60)

        threading.Thread(
            target=windows_helper.paste_text_to_window,
            args=(text, target_hwnd, restore_cb, delay_ms),
            daemon=True
        ).start()

    def reload_csv(self):
        count = self.manager.load_templates()
        self.tray_icon.showMessage(
            "템플릿 갱신 완료",
            f"총 {count}개의 템플릿을 불러왔습니다.",
            QSystemTrayIcon.MessageIcon.Information,
            2000
        )

    def sync_remote(self):
        url = self.manager.config.get("remote_csv_url", "").strip()
        if not url:
            QMessageBox.information(
                None, "구글 시트 연동 안내",
                "config.json의 'remote_csv_url' 항목에 구글 스프레드시트의 '웹에 게시' CSV 링크를 입력해 주세요."
            )
            return

        success, msg = self.manager.sync_from_remote_csv()
        icon = QSystemTrayIcon.MessageIcon.Information if success else QSystemTrayIcon.MessageIcon.Warning
        self.tray_icon.showMessage("구글 시트 동기화", msg, icon, 3000)

    def open_csv_file(self):
        csv_path = os.path.abspath(self.manager.config.get("csv_file", "chat_templates.csv"))
        if os.path.exists(csv_path):
            os.startfile(csv_path)

    def open_config_file(self):
        config_path = os.path.abspath("config.json")
        if os.path.exists(config_path):
            os.startfile(config_path)

    def quit_app(self):
        if self.expander:
            self.expander.stop()
        if self.hotkey_listener:
            try:
                self.hotkey_listener.stop()
            except Exception:
                pass
        self.app.quit()

    def run(self):
        sys.exit(self.app.exec())


if __name__ == "__main__":
    app = ChatHelperApp()
    app.run()
