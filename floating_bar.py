import sys
from typing import List, Tuple
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
    타이핑 중 커서 바로 옆/위를 졸졸 따라다니는 무포커스(Non-activating) 미니 추천 바.
    모드 1: 스마트폰 인라인 툴팁 (1개 칩)
    모드 2: 가로형 미니 추천 바 (2~3개 칩)
    """
    item_selected = pyqtSignal(TemplateItem, int)  # (선택된 아이템, 지울 글자 수)

    def __init__(self):
        super().__init__()
        self.candidates: List[TemplateItem] = []
        self.current_matched_len = 0
        self.target_hwnd = 0

        # 포커스를 절대 뺏지 않는 플래그
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
        root_layout.setContentsMargins(4, 4, 4, 4)
        root_layout.setSpacing(0)

        self.card = QFrame(self)
        self.card.setObjectName("BarCard")
        self.card_layout = QHBoxLayout(self.card)
        self.card_layout.setContentsMargins(8, 4, 8, 4)
        self.card_layout.setSpacing(6)

        # 그림자 효과
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(12)
        shadow.setColor(QColor(0, 0, 0, 160))
        shadow.setOffset(0, 3)
        self.card.setGraphicsEffect(shadow)

        root_layout.addWidget(self.card)

    def show_candidates(self, mode: str, candidates: List[TemplateItem], matched_len: int, target_hwnd: int):
        """
        후보 목록을 화면에 표시하고 커서 위치로 이동
        mode: "inline_pill" (모드 1) 또는 "horizontal_bar" (모드 2)
        """
        if not candidates or mode == "manual_only":
            self.hide()
            return

        self.candidates = candidates
        self.current_matched_len = matched_len
        self.target_hwnd = target_hwnd

        # 기존 아이템 제거
        while self.card_layout.count():
            item = self.card_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.deleteLater()

        if mode == "inline_pill":
            self.render_inline_pill(candidates[0])
        else:
            self.render_horizontal_bar(candidates[:3])

        # 크기 재계산
        self.adjustSize()

        # 커서 위치로 이동
        self.position_near_caret()

        self.show()

        # Win32 무포커스 강제 적용 (팝업 직후 1회)
        windows_helper.make_window_non_activating(int(self.winId()))

    def render_inline_pill(self, top_item: TemplateItem):
        """모드 1: 스마트폰 인라인 툴팁 (단일 칩 형태)"""
        self.card.setStyleSheet("""
            #BarCard {
                background-color: #1e1e24;
                border: 1px solid #3b82f6;
                border-radius: 14px;
            }
        """)

        # 아이콘 & Tab 안내 뱃지
        tab_badge = QLabel("Tab ↵")
        tab_badge.setStyleSheet("""
            color: #ffffff;
            background-color: #2563eb;
            font-size: 11px;
            font-weight: bold;
            border-radius: 8px;
            padding: 2px 7px;
        """)
        self.card_layout.addWidget(tab_badge)

        # 문구 미리보기
        preview = top_item.content.strip().replace("\n", " ")
        if len(preview) > 35:
            preview = preview[:35] + "..."

        text_btn = QPushButton(f"{top_item.title}: \"{preview}\"")
        text_btn.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        text_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        text_btn.setStyleSheet("""
            QPushButton {
                background: transparent;
                color: #f3f4f6;
                font-size: 12px;
                font-weight: 500;
                border: none;
                padding: 2px 4px;
                text-align: left;
            }
            QPushButton:hover {
                color: #60a5fa;
            }
        """)
        text_btn.clicked.connect(lambda: self.on_click_item(top_item))
        self.card_layout.addWidget(text_btn)

    def render_horizontal_bar(self, items: List[TemplateItem]):
        """모드 2: 스마트폰 키보드 상단 가로형 추천 바 (2~3개 칩)"""
        self.card.setStyleSheet("""
            #BarCard {
                background-color: #18181b;
                border: 1px solid #3f3f46;
                border-radius: 8px;
            }
        """)

        for idx, item in enumerate(items):
            # 첫 번째는 [Tab / 1], 이후는 [2], [3]
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
                        border-radius: 5px;
                        padding: 3px 8px;
                    }
                    QPushButton:hover {
                        background-color: #2563eb;
                    }
                """)
            else:
                btn.setStyleSheet("""
                    QPushButton {
                        background-color: #27272a;
                        color: #d4d4d8;
                        font-size: 11px;
                        border: 1px solid #3f3f46;
                        border-radius: 5px;
                        padding: 3px 8px;
                    }
                    QPushButton:hover {
                        background-color: #3f3f46;
                        color: #ffffff;
                    }
                """)

            btn.clicked.connect(lambda checked, it=item: self.on_click_item(it))
            self.card_layout.addWidget(btn)

    def position_near_caret(self):
        """현재 텍스트 커서(Caret) 바로 위 또는 아래로 팝업 위치 이동"""
        cx, cy = windows_helper.get_caret_or_cursor_pos()

        screen = QApplication.screenAt(QPoint(cx, cy))
        if not screen:
            screen = QApplication.primaryScreen()
        screen_geo = screen.availableGeometry()

        w, h = self.width(), self.height()

        # 커서 바로 위에 배치 (스마트폰처럼)
        target_x = cx
        target_y = cy - h - 6

        # 위쪽 공간이 부족하면 커서 아래로 배치
        if target_y < screen_geo.top():
            target_y = cy + 22

        # 오른쪽 경계 초과 방지
        if target_x + w > screen_geo.right():
            target_x = screen_geo.right() - w - 10
        if target_x < screen_geo.left():
            target_x = screen_geo.left() + 10

        self.move(target_x, target_y)

    def on_click_item(self, item: TemplateItem):
        self.item_selected.emit(item, self.current_matched_len)
        self.hide()

    def select_first_candidate(self) -> bool:
        """Tab 키가 눌렸을 때 1번 후보 완성"""
        if self.isVisible() and self.candidates:
            top = self.candidates[0]
            self.item_selected.emit(top, self.current_matched_len)
            self.hide()
            return True
        return False

    def select_candidate_by_index(self, index: int) -> bool:
        """숫자 키(1, 2, 3)가 눌렸을 때 해당 순번 후보 완성"""
        if self.isVisible() and 0 <= index < len(self.candidates):
            chosen = self.candidates[index]
            self.item_selected.emit(chosen, self.current_matched_len)
            self.hide()
            return True
        return False
