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
    """pynput 백그라운드 스레드에서 PyQt 메인 GUI 스레드로 안전하게 신호를 전달하는 브리지"""
    open_overlay_signal = pyqtSignal(int)
    show_tray_message_signal = pyqtSignal(str, str)
    candidates_signal = pyqtSignal(list, int, int)  # candidates, matched_len, target_hwnd


def create_tray_icon() -> QIcon:
    """트레이 아이콘 동적 생성"""
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

        # 커서 추적 무포커스 플로팅 바 (옵션 1 & 2)
        self.floating_bar = FloatingBar()
        self.floating_bar.item_selected.connect(self.handle_floating_bar_selection)

        # 설정 대화상자
        self.settings_dialog = SettingsDialog(self.manager)
        self.settings_dialog.settings_saved.connect(self.on_settings_saved)

        # 4. 텍스트 익스팬더 (실시간 추천 & Tab 자동완성)
        self.expander = TextExpander(
            self.manager,
            on_candidates_callback=self.on_expander_candidates,
            on_tab_complete_callback=self.on_expander_tab_pressed,
            on_expanded_callback=self.on_text_expanded
        )
        self.expander.start()

        # 5. 트레이 아이콘 설정
        self.tray_icon = QSystemTrayIcon(create_tray_icon(), self.app)
        self.setup_tray_menu()
        self.tray_icon.show()

        # 6. 글로벌 핫키 리스너
        self.hotkey_listener = None
        self.start_hotkey_listener()

        # 시작 알림
        mode_name = "인라인 툴팁 (Tab 완성)" if self.manager.config.get("floating_bar_mode") == "inline_pill" else "가로형 추천 바"
        self.tray_icon.showMessage(
            "채팅 도우미 시작됨",
            f"현재 모드: [{mode_name}]\n트레이 아이콘에서 [설정]을 눌러 모드를 변경해 비교해 보세요!",
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
        self.start_hotkey_listener()
        mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        mode_label = "옵션 1: 인라인 툴팁" if mode == "inline_pill" else ("옵션 2: 가로형 추천 바" if mode == "horizontal_bar" else "옵션 3: 수동 모드")
        self.tray_icon.showMessage("설정 변경 완료", f"현재 모드가 '{mode_label}'로 적용되었습니다.", QSystemTrayIcon.MessageIcon.Information, 2000)

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

    # 텍스트 익스팬더 콜백들
    def on_expander_candidates(self, candidates: List[TemplateItem], matched_len: int, target_hwnd: int):
        self.bridge.candidates_signal.emit(candidates, matched_len, target_hwnd)

    def on_candidates_received(self, candidates: list, matched_len: int, target_hwnd: int):
        mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        if not candidates or mode == "manual_only" or self.overlay.isVisible():
            self.floating_bar.hide()
            return
        self.floating_bar.show_candidates(mode, candidates, matched_len, target_hwnd)

    def on_expander_tab_pressed(self) -> bool:
        """Tab 키가 눌렸을 때 플로팅 바가 켜져 있으면 자동완성 처리"""
        if self.floating_bar.isVisible():
            return self.floating_bar.select_first_candidate()
        return False

    def handle_floating_bar_selection(self, item: TemplateItem, delete_count: int):
        target_hwnd = self.floating_bar.target_hwnd or windows_helper.get_foreground_window()
        restore_cb = self.manager.config.get("restore_clipboard", True)

        def _worker():
            windows_helper.replace_text_at_cursor(
                delete_count=delete_count,
                new_text=item.content,
                target_hwnd=target_hwnd,
                restore_clipboard=restore_cb
            )

        threading.Thread(target=_worker, daemon=True).start()

    def on_text_expanded(self, title: str):
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
                "설정에서 구글 스프레드시트의 '웹에 게시' CSV 링크를 입력해 주세요."
            )
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
