import os
import sys
import threading
import time
from typing import List
from PyQt6.QtCore import QObject, pyqtSignal, QTimer, Qt
from PyQt6.QtWidgets import (
    QApplication, QSystemTrayIcon, QMenu, QMessageBox
)
from PyQt6.QtGui import QIcon, QPixmap, QPainter, QColor, QFont
from pynput import keyboard

from template_manager import TemplateManager, TemplateItem
from overlay_ui import OverlayWindow
from floating_bar import FloatingBar
from settings_dialog import SettingsDialog
from text_expander import TextExpander
import windows_helper


class HotkeyBridge(QObject):
    open_overlay_signal = pyqtSignal(int)
    show_tray_message_signal = pyqtSignal(str, str)
    candidates_signal = pyqtSignal(list, int, int)


def create_tray_icon() -> QIcon:
    pixmap = QPixmap(32, 32)
    pixmap.fill(Qt.GlobalColor.transparent)

    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)

    painter.setBrush(QColor("#2563eb"))
    painter.setPen(Qt.PenStyle.NoPen)
    painter.drawRoundedRect(2, 2, 28, 28, 7, 7)

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

        # 2. 브리지
        self.bridge = HotkeyBridge()
        self.bridge.open_overlay_signal.connect(self.on_hotkey_triggered)
        self.bridge.show_tray_message_signal.connect(self.on_show_tray_message)
        self.bridge.candidates_signal.connect(self.on_candidates_received)

        # 3. UI 윈도우들
        self.overlay = OverlayWindow(self.manager)
        self.overlay.request_paste.connect(self.handle_paste_request)
        self.overlay.open_settings_signal.connect(self.open_settings)
        self.overlay.mode_changed_signal.connect(self.on_mode_switched)

        self.floating_bar = FloatingBar()
        self.floating_bar.item_selected.connect(self.handle_floating_bar_selection)

        self.settings_dialog = SettingsDialog(self.manager)
        self.settings_dialog.settings_saved.connect(self.on_settings_saved)

        # 4. 초고속 텍스트 익스팬더
        self.expander = TextExpander(
            self.manager,
            on_candidates_callback=self.on_expander_candidates,
            on_tab_complete_callback=self.on_expander_tab_pressed,
            on_expanded_callback=self.on_text_expanded
        )
        self.expander.start()

        # 5. 트레이 아이콘
        self.tray_icon = QSystemTrayIcon(create_tray_icon(), self.app)
        self.setup_tray_menu()
        self.tray_icon.show()

        # 6. 글로벌 핫키 리스너
        self.hotkey_listener = None
        self.start_hotkey_listener()

        # 시작 알림
        mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        mode_str = "옵션 1 (인라인 툴팁)" if mode == "inline_pill" else ("옵션 2 (가로 추천바)" if mode == "horizontal_bar" else "옵션 3 (수동)")
        self.tray_icon.showMessage(
            "채팅 도우미 시작됨 (초고속 반응 적용)",
            f"현재 모드: [{mode_str}]\nAlt+Q 창 상단에서 모드를 바로 바꿀 수 있습니다!",
            QSystemTrayIcon.MessageIcon.Information,
            3000
        )

    def setup_tray_menu(self):
        menu = QMenu()
        menu.setStyleSheet("""
            QMenu { background-color: #27272a; color: #f4f4f5; border: 1px solid #3f3f46; padding: 4px; }
            QMenu::item { padding: 6px 20px; border-radius: 4px; }
            QMenu::item:selected { background-color: #2563eb; }
            QMenu::separator { height: 1px; background: #3f3f46; margin: 4px 8px; }
        """)

        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")
        action_open = menu.addAction(f"자주 쓰는 말 열기 ({hotkey_str})")
        action_open.triggered.connect(self.trigger_overlay_manual)

        action_settings = menu.addAction("⚙️ 자동완성 모드 설정...")
        action_settings.triggered.connect(self.open_settings)

        menu.addSeparator()

        action_reload = menu.addAction("CSV 템플릿 다시 불러오기")
        action_reload.triggered.connect(self.reload_csv)

        action_sync = menu.addAction("구글 시트에서 최신 동기화")
        action_sync.triggered.connect(self.sync_remote)

        menu.addSeparator()

        action_open_csv = menu.addAction("CSV 템플릿 파일 열기")
        action_open_csv.triggered.connect(self.open_csv_file)

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
        self.overlay.show_overlay_at_cursor(hwnd)

    def open_settings(self):
        self.settings_dialog.load_values()
        self.settings_dialog.show()
        self.settings_dialog.raise_()
        self.settings_dialog.activateWindow()

    def on_settings_saved(self):
        self.floating_bar.hide()
        self.overlay.update_mode_buttons()
        self.start_hotkey_listener()
        mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        mode_label = "옵션 1: 인라인 툴팁" if mode == "inline_pill" else ("옵션 2: 가로형 추천 바" if mode == "horizontal_bar" else "옵션 3: 수동 모드")
        self.tray_icon.showMessage("설정 적용 완료", f"모드가 '{mode_label}'로 변경되었습니다.", QSystemTrayIcon.MessageIcon.Information, 1500)

    def on_mode_switched(self, new_mode: str):
        self.floating_bar.hide()
        mode_label = "옵션 1 (인라인 툴팁)" if new_mode == "inline_pill" else ("옵션 2 (가로형 추천바)" if new_mode == "horizontal_bar" else "옵션 3 (수동)")
        self.tray_icon.showMessage("모드 전환", f"'{mode_label}' 적용됨", QSystemTrayIcon.MessageIcon.Information, 1000)

    def start_hotkey_listener(self):
        if self.hotkey_listener:
            try:
                self.hotkey_listener.stop()
            except Exception:
                pass

        hotkey_str = self.manager.config.get("hotkey", "<alt>+q")

        def on_activate():
            hwnd = windows_helper.get_foreground_window()
            self.bridge.open_overlay_signal.emit(hwnd)

        try:
            hotkey_mapping = {hotkey_str: on_activate}
            self.hotkey_listener = keyboard.GlobalHotKeys(hotkey_mapping)
            self.hotkey_listener.daemon = True
            self.hotkey_listener.start()
        except Exception as e:
            print(f"[단축키 등록 실패] {e}")

    def on_hotkey_triggered(self, hwnd: int):
        self.floating_bar.hide()
        if self.overlay.isVisible():
            self.overlay.hide_overlay()
        else:
            self.overlay.show_overlay_at_cursor(hwnd)

    def on_expander_candidates(self, candidates: List[TemplateItem], matched_len: int, target_hwnd: int):
        self.bridge.candidates_signal.emit(candidates, matched_len, target_hwnd)

    def on_candidates_received(self, candidates: list, matched_len: int, target_hwnd: int):
        mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        if not candidates or mode == "manual_only" or self.overlay.isVisible():
            self.floating_bar.hide()
            return
        self.floating_bar.show_candidates(mode, candidates, matched_len, target_hwnd)

    def on_expander_tab_pressed(self) -> bool:
        if self.floating_bar.isVisible():
            return self.floating_bar.select_first_candidate()
        return False

    def handle_floating_bar_selection(self, item: TemplateItem, delete_count: int):
        target_hwnd = self.floating_bar.target_hwnd or windows_helper.get_foreground_window()

        def _worker():
            windows_helper.replace_text_at_cursor(
                delete_count=delete_count,
                new_text=item.content,
                target_hwnd=target_hwnd,
                restore_clipboard=False
            )

        threading.Thread(target=_worker, daemon=True).start()

    def on_text_expanded(self, title: str):
        self.bridge.show_tray_message_signal.emit("단축어 자동완성", f"'{title}' 완성됨")

    def on_show_tray_message(self, title: str, msg: str):
        self.tray_icon.showMessage(title, msg, QSystemTrayIcon.MessageIcon.Information, 1000)

    def handle_paste_request(self, text: str, target_hwnd: int):
        def _worker():
            windows_helper.fast_paste_text(text, target_hwnd)

        threading.Thread(target=_worker, daemon=True).start()

    def reload_csv(self):
        count = self.manager.load_templates()
        self.tray_icon.showMessage("템플릿 갱신", f"총 {count}개 템플릿 로드 완료", QSystemTrayIcon.MessageIcon.Information, 1500)

    def sync_remote(self):
        url = self.manager.config.get("remote_csv_url", "").strip()
        if not url:
            QMessageBox.information(None, "안내", "설정에서 구글 시트 CSV 링크를 먼저 입력해 주세요.")
            return

        success, msg = self.manager.sync_from_remote_csv()
        icon = QSystemTrayIcon.MessageIcon.Information if success else QSystemTrayIcon.MessageIcon.Warning
        self.tray_icon.showMessage("구글 시트 동기화", msg, icon, 3000)

    def open_csv_file(self):
        csv_path = os.path.abspath(self.manager.config.get("csv_file", "chat_templates.csv"))
        if os.path.exists(csv_path):
            os.startfile(csv_path)

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
