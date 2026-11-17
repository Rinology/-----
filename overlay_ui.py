import sys
from PyQt6.QtCore import Qt, QPoint, pyqtSignal, QEvent
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLineEdit, QListWidget,
    QListWidgetItem, QLabel, QFrame, QGraphicsDropShadowEffect,
    QApplication
)
from PyQt6.QtGui import QColor, QFont, QKeyEvent
from template_manager import TemplateManager, TemplateItem
import windows_helper


class SuggestionItemWidget(QWidget):
    """각 제안 항목의 커스텀 UI 위젯"""
    def __init__(self, item: TemplateItem, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(10, 6, 10, 6)
        layout.setSpacing(3)

        top_layout = QHBoxLayout()
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(8)

        # 키워드 뱃지
        self.badge_label = QLabel(f"[{item.keyword}]" if item.keyword else "")
        self.badge_label.setStyleSheet("""
            color: #60a5fa;
            font-weight: bold;
            font-size: 12px;
        """)
        top_layout.addWidget(self.badge_label)

        # 제목
        self.title_label = QLabel(item.title)
        self.title_label.setStyleSheet("""
            color: #f3f4f6;
            font-weight: 600;
            font-size: 13px;
        """)
        top_layout.addWidget(self.title_label)

        # 카테고리 태그
        if item.category and item.category != "일반":
            cat_label = QLabel(item.category)
            cat_label.setStyleSheet("""
                color: #9ca3af;
                background-color: #374151;
                border-radius: 4px;
                padding: 1px 6px;
                font-size: 11px;
            """)
            top_layout.addWidget(cat_label)

        top_layout.addStretch()
        layout.addLayout(top_layout)

        # 내용 미리보기 (첫 줄 또는 60글자 요약)
        preview_text = item.content.replace("\n", "  ↵  ")
        if len(preview_text) > 70:
            preview_text = preview_text[:70] + "..."
        self.content_label = QLabel(preview_text)
        self.content_label.setStyleSheet("""
            color: #9ca3af;
            font-size: 11px;
        """)
        layout.addWidget(self.content_label)


class OverlayWindow(QWidget):
    request_paste = pyqtSignal(str, int)  # 텍스트, target_hwnd

    def __init__(self, manager: TemplateManager):
        super().__init__()
        self.manager = manager
        self.target_hwnd = 0

        # 창 속성: 프레임리스, 항상 최상위, 작업표시줄 제외
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint |
            Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setFixedSize(560, 420)

        self.init_ui()

    def init_ui(self):
        root_layout = QVBoxLayout(self)
        root_layout.setContentsMargins(12, 12, 12, 12)

        # 메인 카드 컨테이너 (그림자 효과를 위한 박스)
        self.card = QFrame(self)
        self.card.setObjectName("MainCard")
        self.card.setStyleSheet("""
            #MainCard {
                background-color: #1e1e24;
                border: 1px solid #3f3f46;
                border-radius: 12px;
            }
        """)

        # 그림자 효과
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(24)
        shadow.setColor(QColor(0, 0, 0, 160))
        shadow.setOffset(0, 8)
        self.card.setGraphicsEffect(shadow)

        card_layout = QVBoxLayout(self.card)
        card_layout.setContentsMargins(14, 14, 14, 12)
        card_layout.setSpacing(10)

        # 1. 상단 검색창
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("🔍 자주 쓰는 말 검색... (키워드/제목/내용)")
        self.search_input.setFixedHeight(40)
        self.search_input.setStyleSheet("""
            QLineEdit {
                background-color: #27272a;
                color: #f4f4f5;
                border: 1px solid #52525b;
                border-radius: 8px;
                padding: 0 12px;
                font-size: 14px;
            }
            QLineEdit:focus {
                border: 1.5px solid #3b82f6;
            }
        """)
        self.search_input.textChanged.connect(self.on_search_changed)
        card_layout.addWidget(self.search_input)

        # 2. 제안 리스트
        self.list_widget = QListWidget()
        self.list_widget.setStyleSheet("""
            QListWidget {
                background-color: transparent;
                border: none;
                outline: none;
            }
            QListWidget::item {
                background-color: #27272a;
                border-radius: 8px;
                margin-bottom: 5px;
            }
            QListWidget::item:selected {
                background-color: #1e3a8a;
                border: 1px solid #3b82f6;
            }
            QListWidget::item:hover:!selected {
                background-color: #323238;
            }
        """)
        self.list_widget.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.list_widget.itemDoubleClicked.connect(self.select_current_item)
        card_layout.addWidget(self.list_widget)

        # 3. 하단 상태 및 팁 바
        bottom_layout = QHBoxLayout()
        bottom_layout.setContentsMargins(4, 0, 4, 0)

        self.tip_label = QLabel("↵ Enter: 입력  |  ↑/↓: 이동  |  Esc: 닫기  |  Ctrl+R: 새로고침")
        self.tip_label.setStyleSheet("color: #71717a; font-size: 11px;")
        bottom_layout.addWidget(self.tip_label)

        bottom_layout.addStretch()

        self.count_label = QLabel("0개")
        self.count_label.setStyleSheet("color: #a1a1aa; font-size: 11px;")
        bottom_layout.addWidget(self.count_label)

        card_layout.addLayout(bottom_layout)
        root_layout.addWidget(self.card)

        # 검색창에서 위/아래 방향키 및 엔터 키를 처리하기 위한 이벤트 필터 설치
        self.search_input.installEventFilter(self)

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

    def show_overlay(self, previous_hwnd: int):
        """이전 활성 창(카카오톡, 브라우저 등)의 핸들을 저장하고 화면 중앙에 팝업"""
        self.target_hwnd = previous_hwnd

        # 화면 중앙 배치
        screen = QApplication.primaryScreen().geometry()
        x = (screen.width() - self.width()) // 2
        y = (screen.height() - self.height()) // 2
        self.move(x, y)

        # 검색창 초기화 및 포커스
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

        for item in matched:
            list_item = QListWidgetItem(self.list_widget)
            widget = SuggestionItemWidget(item)
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

        # 먼저 오버레이를 숨김
        self.hide_overlay()

        # 시그널 발생 -> 메인 스레드 또는 핸들러가 대상 창에 텍스트 붙여넣기
        self.request_paste.emit(content, target_hwnd)

    def refresh_templates(self):
        count = self.manager.load_templates()
        self.update_list(self.search_input.text())
        self.tip_label.setText(f"새로고침 완료! ({count}개 로드됨)")
