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
import windows_helper


class HotkeyBridge(QObject):
    """pynput 스레드에서 PyQt 메인 GUI 스레드로 신호를 전달하는 브리지"""
    open_overlay_signal = pyqtSignal(int)


def create_tray_icon() -> QIcon:
    """트레이 아이콘 동적 생성 (별도 이미지 파일 없이 렌더링)"""
    pixmap = QPixmap(32, 32)
    pixmap.fill(Qt.GlobalColor.transparent)

    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)

    # 둥근 파란색 배경
    painter.setBrush(QColor("#3b82f6"))
    painter.setPen(Qt.PenStyle.NoPen)
    painter.drawRoundedRect(2, 2, 28, 28, 6, 6)

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

        # 템플릿 관리자
        self.manager = TemplateManager("config.json")

        # 핫키 브리지 및 오버레이 윈도우
        self.bridge = HotkeyBridge()
        self.bridge.open_overlay_signal.connect(self.on_hotkey_triggered)

        self.overlay = OverlayWindow(self.manager)
        self.overlay.request_paste.connect(self.handle_paste_request)

        # 트레이 아이콘 설정
        self.tray_icon = QSystemTrayIcon(create_tray_icon(), self.app)
        self.setup_tray_menu()
        self.tray_icon.show()

        # 글로벌 핫키 리스너 시작
        self.hotkey_listener = None
        self.start_hotkey_listener()

        # 시작 알림
        hotkey_name = self.manager.config.get("hotkey", "<alt>+q")
        self.tray_icon.showMessage(
            "채팅 도우미 시작됨",
            f"단축키 [{hotkey_name}]를 누르면 제안 창이 열립니다.\n트레이 아이콘에서 우클릭하여 설정할 수 있습니다.",
            QSystemTrayIcon.MessageIcon.Information,
            3000
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
                background-color: #3b82f6;
            }
            QMenu::separator {
                height: 1px;
                background: #3f3f46;
                margin: 4px 8px;
            }
        """)

        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")
        action_open = menu.addAction(f"자주 쓰는 말 열기 ({hotkey_str})")
        action_open.triggered.connect(lambda: self.trigger_overlay_manual())

        action_reload = menu.addAction("CSV 파일 다시 불러오기")
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
        self.tray_icon.setToolTip(f"채팅 도우미 (단축키: {hotkey_str})")
        self.tray_icon.activated.connect(self.on_tray_activated)

    def on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self.trigger_overlay_manual()

    def trigger_overlay_manual(self):
        hwnd = windows_helper.get_foreground_window()
        self.overlay.show_overlay(hwnd)

    def start_hotkey_listener(self):
        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")

        def on_activate():
            # 단축키가 눌렸을 때의 활성 창(포커스)을 캡처
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
            self.tray_icon.showMessage(
                "단축키 등록 실패",
                f"단축키 '{hotkey_str}' 등록 중 오류가 발생했습니다: {e}",
                QSystemTrayIcon.MessageIcon.Warning,
                4000
            )

    def on_hotkey_triggered(self, hwnd: int):
        if self.overlay.isVisible():
            self.overlay.hide_overlay()
        else:
            self.overlay.show_overlay(hwnd)

    def handle_paste_request(self, text: str, target_hwnd: int):
        restore_cb = self.manager.config.get("restore_clipboard", True)
        delay_ms = self.manager.config.get("paste_delay_ms", 70)

        # 별도 스레드에서 붙여넣기 실행 (GUI 멈춤 방지)
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
