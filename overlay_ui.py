import sys
from PyQt6.QtCore import Qt, QPoint, pyqtSignal, QEvent
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLineEdit, QListWidget,
    QListWidgetItem, QLabel, QFrame, QPushButton, QGraphicsDropShadowEffect,
    QApplication
)
from PyQt6.QtGui import QColor, QFont, QKeyEvent
from template_manager import TemplateManager, TemplateItem
import windows_helper


class SuggestionItemWidget(QWidget):
    """각 제안 항목의 커스텀 UI 위젯"""
    def __init__(self, index: int, item: TemplateItem, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 5, 8, 5)
        layout.setSpacing(2)

        top_layout = QHBoxLayout()
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(6)

        if index < 9:
            num_label = QLabel(f"{index + 1}")
            num_label.setStyleSheet("""
                color: #e4e4e7;
                background-color: #3f3f46;
                border-radius: 3px;
                padding: 1px 5px;
                font-size: 11px;
                font-weight: bold;
            """)
            top_layout.addWidget(num_label)

        if item.keyword:
            self.badge_label = QLabel(f"[{item.keyword}]")
            self.badge_label.setStyleSheet("""
                color: #60a5fa;
                font-weight: bold;
                font-size: 12px;
            """)
            top_layout.addWidget(self.badge_label)

        self.title_label = QLabel(item.title)
        self.title_label.setStyleSheet("""
            color: #f4f4f5;
            font-weight: 600;
            font-size: 12px;
        """)
        top_layout.addWidget(self.title_label)

        if item.category and item.category != "일반":
            cat_label = QLabel(item.category)
            cat_label.setStyleSheet("""
                color: #a1a1aa;
                background-color: #27272a;
                border: 1px solid #3f3f46;
                border-radius: 3px;
                padding: 0 4px;
                font-size: 10px;
            """)
            top_layout.addWidget(cat_label)

        top_layout.addStretch()
        layout.addLayout(top_layout)

        preview_text = item.content.strip().replace("\n", "  ↵  ")
        if len(preview_text) > 65:
            preview_text = preview_text[:65] + "..."
        self.content_label = QLabel(preview_text)
        self.content_label.setStyleSheet("color: #9ca3af; font-size: 11px; padding-left: 2px;")
        layout.addWidget(self.content_label)


class OverlayWindow(QWidget):
    request_paste = pyqtSignal(str, int)
    open_settings_signal = pyqtSignal()
    mode_changed_signal = pyqtSignal(str)

    def __init__(self, manager: TemplateManager):
        super().__init__()
        self.manager = manager
        self.target_hwnd = 0

        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint |
            Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setFixedSize(460, 340)

        self.init_ui()

    def init_ui(self):
        root_layout = QVBoxLayout(self)
        root_layout.setContentsMargins(8, 8, 8, 8)

        self.card = QFrame(self)
        self.card.setObjectName("MainCard")
        self.card.setStyleSheet("""
            #MainCard {
                background-color: #18181b;
                border: 1px solid #3f3f46;
                border-radius: 10px;
            }
        """)

        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(20)
        shadow.setColor(QColor(0, 0, 0, 180))
        shadow.setOffset(0, 6)
        self.card.setGraphicsEffect(shadow)

        card_layout = QVBoxLayout(self.card)
        card_layout.setContentsMargins(10, 10, 10, 8)
        card_layout.setSpacing(6)

        # 1. 상단: 모드 즉시 전환 탭 버튼 (옵션 1 / 옵션 2 / 수동)
        top_bar = QHBoxLayout()
        top_bar.setContentsMargins(0, 0, 0, 2)
        top_bar.setSpacing(4)

        self.btn_mode1 = QPushButton("💡 옵션1 (인라인 툴팁)")
        self.btn_mode2 = QPushButton("📊 옵션2 (가로 추천바)")
        self.btn_mode3 = QPushButton("⌨️ 옵션3 (수동)")

        for btn in (self.btn_mode1, self.btn_mode2, self.btn_mode3):
            btn.setFixedHeight(24)
            btn.setCursor(Qt.CursorShape.PointingHandCursor)

        self.btn_mode1.clicked.connect(lambda: self.switch_mode("inline_pill"))
        self.btn_mode2.clicked.connect(lambda: self.switch_mode("horizontal_bar"))
        self.btn_mode3.clicked.connect(lambda: self.switch_mode("manual_only"))

        top_bar.addWidget(self.btn_mode1)
        top_bar.addWidget(self.btn_mode2)
        top_bar.addWidget(self.btn_mode3)

        btn_settings = QPushButton("⚙️")
        btn_settings.setToolTip("상세 설정 열기")
        btn_settings.setFixedSize(26, 24)
        btn_settings.setStyleSheet("""
            QPushButton {
                background-color: #27272a;
                color: #e4e4e7;
                border: 1px solid #3f3f46;
                border-radius: 4px;
            }
            QPushButton:hover { background-color: #3f3f46; }
        """)
        btn_settings.clicked.connect(self.on_click_settings)
        top_bar.addWidget(btn_settings)

        card_layout.addLayout(top_bar)

        # 2. 검색창
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("💬 검색 또는 1~9 숫자 선택 (Enter 완성)")
        self.search_input.setFixedHeight(34)
        self.search_input.setStyleSheet("""
            QLineEdit {
                background-color: #27272a;
                color: #f4f4f5;
                border: 1px solid #52525b;
                border-radius: 6px;
                padding: 0 10px;
                font-size: 13px;
            }
            QLineEdit:focus { border: 1.5px solid #3b82f6; }
        """)
        self.search_input.textChanged.connect(self.on_search_changed)
        card_layout.addWidget(self.search_input)

        # 3. 제안 리스트
        self.list_widget = QListWidget()
        self.list_widget.setStyleSheet("""
            QListWidget { background-color: transparent; border: none; outline: none; }
            QListWidget::item { background-color: #27272a; border-radius: 6px; margin-bottom: 4px; }
            QListWidget::item:selected { background-color: #1e3a8a; border: 1px solid #3b82f6; }
            QListWidget::item:hover:!selected { background-color: #323238; }
        """)
        self.list_widget.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.list_widget.itemDoubleClicked.connect(self.select_current_item)
        card_layout.addWidget(self.list_widget)

        # 4. 하단 상태 바
        bottom_layout = QHBoxLayout()
        bottom_layout.setContentsMargins(2, 0, 2, 0)

        self.tip_label = QLabel("↵ Enter: 입력  |  Esc: 닫기  |  Ctrl+R: 새로고침")
        self.tip_label.setStyleSheet("color: #71717a; font-size: 11px;")
        bottom_layout.addWidget(self.tip_label)

        bottom_layout.addStretch()

        self.count_label = QLabel("0개")
        self.count_label.setStyleSheet("color: #a1a1aa; font-size: 11px;")
        bottom_layout.addWidget(self.count_label)

        card_layout.addLayout(bottom_layout)
        root_layout.addWidget(self.card)

        self.search_input.installEventFilter(self)
        self.update_mode_buttons()

    def update_mode_buttons(self):
        cur_mode = self.manager.config.get("floating_bar_mode", "inline_pill")
        active_style = "background-color: #2563eb; color: #ffffff; border: 1px solid #3b82f6; border-radius: 4px; font-size: 11px; font-weight: bold;"
        inactive_style = "background-color: #27272a; color: #a1a1aa; border: 1px solid #3f3f46; border-radius: 4px; font-size: 11px;"

        self.btn_mode1.setStyleSheet(active_style if cur_mode == "inline_pill" else inactive_style)
        self.btn_mode2.setStyleSheet(active_style if cur_mode == "horizontal_bar" else inactive_style)
        self.btn_mode3.setStyleSheet(active_style if cur_mode == "manual_only" else inactive_style)

    def switch_mode(self, mode: str):
        self.manager.config["floating_bar_mode"] = mode
        self.manager.save_config()
        self.update_mode_buttons()
        self.mode_changed_signal.emit(mode)
        mode_label = "옵션 1 (인라인 툴팁)" if mode == "inline_pill" else ("옵션 2 (가로 추천바)" if mode == "horizontal_bar" else "옵션 3 (수동)")
        self.tip_label.setText(f"모드 전환됨: {mode_label}")

    def on_click_settings(self):
        self.hide_overlay()
        self.open_settings_signal.emit()

    def eventFilter(self, source, event):
        if source == self.search_input and event.type() == QEvent.Type.KeyPress:
            key = event.key()

            if key == Qt.Key.Key_Down:
                cur_row = self.list_widget.currentRow()
                if cur_row < self.list_widget.count() - 1:
                    self.list_widget.setCurrentRow(cur_row + 1)
                return True
            elif key == Qt.Key.Key_Up:
                cur_row = self.list_widget.currentRow()
                if cur_row > 0:
                    self.list_widget.setCurrentRow(cur_row - 1)
                return True
            elif key in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
                self.select_current_item()
                return True
            elif key == Qt.Key.Key_Escape:
                self.hide_overlay()
                return True
            elif not self.search_input.text() and Qt.Key.Key_1 <= key <= Qt.Key.Key_9:
                idx = key - Qt.Key.Key_1
                if idx < self.list_widget.count():
                    self.list_widget.setCurrentRow(idx)
                    self.select_current_item()
                    return True
            elif key == Qt.Key.Key_R and (event.modifiers() & Qt.KeyboardModifier.ControlModifier):
                self.refresh_templates()
                return True

        return super().eventFilter(source, event)

    def keyPressEvent(self, event: QKeyEvent):
        if event.key() == Qt.Key.Key_Escape:
            self.hide_overlay()
        elif event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
            self.select_current_item()
        else:
            super().keyPressEvent(event)

    def show_overlay_at_cursor(self, previous_hwnd: int):
        self.target_hwnd = previous_hwnd
        self.update_mode_buttons()

        cx, cy = windows_helper.get_caret_or_cursor_pos()
        screen = QApplication.screenAt(QPoint(cx, cy)) or QApplication.primaryScreen()
        screen_geo = screen.availableGeometry()

        w, h = self.width(), self.height()
        target_x = cx
        target_y = cy + 20

        if target_x + w > screen_geo.right():
            target_x = screen_geo.right() - w - 10
        if target_x < screen_geo.left():
            target_x = screen_geo.left() + 10

        if target_y + h > screen_geo.bottom():
            target_y = cy - h - 10
        if target_y < screen_geo.top():
            target_y = screen_geo.top() + 10

        self.move(target_x, target_y)
        self.search_input.clear()
        self.update_list("")

        self.show()
        self.raise_()
        self.activateWindow()
        self.search_input.setFocus()

    def hide_overlay(self):
        self.hide()

    def on_search_changed(self, text: str):
        self.update_list(text)

    def update_list(self, query: str):
        self.list_widget.clear()
        matched = self.manager.search(query)
        self.count_label.setText(f"{len(matched)}개")

        for idx, item in enumerate(matched):
            list_item = QListWidgetItem(self.list_widget)
            widget = SuggestionItemWidget(idx, item)
            list_item.setSizeHint(widget.sizeHint())
            list_item.setData(Qt.ItemDataRole.UserRole, item)
            self.list_widget.addItem(list_item)
            self.list_widget.setItemWidget(list_item, widget)

        if self.list_widget.count() > 0:
            self.list_widget.setCurrentRow(0)

    def select_current_item(self):
        current = self.list_widget.currentItem()
        if not current:
            self.hide_overlay()
            return

        item: TemplateItem = current.data(Qt.ItemDataRole.UserRole)
        content = item.content
        target_hwnd = self.target_hwnd

        self.hide_overlay()
        self.request_paste.emit(content, target_hwnd)

    def refresh_templates(self):
        count = self.manager.load_templates()
        self.update_list(self.search_input.text())
        self.tip_label.setText(f"새로고침 완료! ({count}개 로드됨)")
