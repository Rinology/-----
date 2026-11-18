import sys
from typing import List
from PyQt6.QtCore import Qt, QPoint, pyqtSignal
from PyQt6.QtWidgets import (
    QWidget, QHBoxLayout, QLabel, QFrame, QPushButton,
    QGraphicsDropShadowEffect, QApplication
)
from PyQt6.QtGui import QColor, QFont, QCursor
from template_manager import TemplateItem
import windows_helper


class FloatingBar(QWidget):
    """
    타이핑 중 커서 바로 옆/위를 초고속으로 따라다니는 무포커스(Non-activating) 미니 추천 바.
    모드 1: 스마트폰 인라인 툴팁 (1개 칩)
    모드 2: 가로형 미니 추천 바 (2~3개 칩)
    """
    item_selected = pyqtSignal(TemplateItem, int)

    def __init__(self):
        super().__init__()
        self.candidates: List[TemplateItem] = []
        self.current_matched_len = 0
        self.target_hwnd = 0

        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint |
            Qt.WindowType.Tool |
            Qt.WindowType.WindowDoesNotAcceptFocus
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)

        self.init_ui()

    def init_ui(self):
        root_layout = QHBoxLayout(self)
        root_layout.setContentsMargins(2, 2, 2, 2)
        root_layout.setSpacing(0)

        self.card = QFrame(self)
        self.card.setObjectName("BarCard")
        self.card_layout = QHBoxLayout(self.card)
        self.card_layout.setContentsMargins(6, 3, 6, 3)
        self.card_layout.setSpacing(4)

        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(10)
        shadow.setColor(QColor(0, 0, 0, 160))
        shadow.setOffset(0, 2)
        self.card.setGraphicsEffect(shadow)

        root_layout.addWidget(self.card)

    def show_candidates(self, mode: str, candidates: List[TemplateItem], matched_len: int, target_hwnd: int):
        if not candidates or mode == "manual_only":
            self.hide()
            return

        self.candidates = candidates
        self.current_matched_len = matched_len
        self.target_hwnd = target_hwnd

        while self.card_layout.count():
            item = self.card_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

        if mode == "inline_pill":
            self.render_inline_pill(candidates[0])
        else:
            self.render_horizontal_bar(candidates[:3])

        self.adjustSize()
        self.position_near_caret()

        if not self.isVisible():
            self.show()
            windows_helper.make_window_non_activating(int(self.winId()))

    def render_inline_pill(self, top_item: TemplateItem):
        self.card.setStyleSheet("""
            #BarCard {
                background-color: #1e1e24;
                border: 1px solid #3b82f6;
                border-radius: 12px;
            }
        """)

        tab_badge = QLabel("Tab ↵")
        tab_badge.setStyleSheet("""
            color: #ffffff;
            background-color: #2563eb;
            font-size: 10px;
            font-weight: bold;
            border-radius: 6px;
            padding: 1px 6px;
        """)
        self.card_layout.addWidget(tab_badge)

        preview = top_item.content.strip().replace("\n", " ")
        if len(preview) > 30:
            preview = preview[:30] + "..."

        text_btn = QPushButton(f"{top_item.title}: \"{preview}\"")
        text_btn.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        text_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        text_btn.setStyleSheet("""
            QPushButton {
                background: transparent;
                color: #f3f4f6;
                font-size: 11px;
                font-weight: 500;
                border: none;
                padding: 1px 3px;
                text-align: left;
            }
            QPushButton:hover { color: #60a5fa; }
        """)
        text_btn.clicked.connect(lambda: self.on_click_item(top_item))
        self.card_layout.addWidget(text_btn)

    def render_horizontal_bar(self, items: List[TemplateItem]):
        self.card.setStyleSheet("""
            #BarCard {
                background-color: #18181b;
                border: 1px solid #3f3f46;
                border-radius: 7px;
            }
        """)

        for idx, item in enumerate(items):
            key_hint = "Tab" if idx == 0 else f"{idx + 1}"
            btn = QPushButton(f"[{key_hint}] {item.title}")
            btn.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
            btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
            if idx == 0:
                btn.setStyleSheet("""
                    QPushButton {
                        background-color: #1e3a8a;
                        color: #ffffff;
                        font-size: 11px;
                        font-weight: bold;
                        border: 1px solid #3b82f6;
                        border-radius: 4px;
                        padding: 2px 7px;
                    }
                    QPushButton:hover { background-color: #2563eb; }
                """)
            else:
                btn.setStyleSheet("""
                    QPushButton {
                        background-color: #27272a;
                        color: #d4d4d8;
                        font-size: 11px;
                        border: 1px solid #3f3f46;
                        border-radius: 4px;
                        padding: 2px 7px;
                    }
                    QPushButton:hover { background-color: #3f3f46; color: #ffffff; }
                """)

            btn.clicked.connect(lambda checked, it=item: self.on_click_item(it))
            self.card_layout.addWidget(btn)

    def position_near_caret(self):
        cx, cy = windows_helper.get_caret_or_cursor_pos()
        screen = QApplication.screenAt(QPoint(cx, cy)) or QApplication.primaryScreen()
        screen_geo = screen.availableGeometry()

        w, h = self.width(), self.height()
        target_x = cx
        target_y = cy - h - 4

        if target_y < screen_geo.top():
            target_y = cy + 20

        if target_x + w > screen_geo.right():
            target_x = screen_geo.right() - w - 8
        if target_x < screen_geo.left():
            target_x = screen_geo.left() + 8

        self.move(target_x, target_y)

    def on_click_item(self, item: TemplateItem):
        self.item_selected.emit(item, self.current_matched_len)
        self.hide()

    def select_first_candidate(self) -> bool:
        if self.isVisible() and self.candidates:
            top = self.candidates[0]
            self.item_selected.emit(top, self.current_matched_len)
            self.hide()
            return True
        return False
