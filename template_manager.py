import csv
import json
import os
import requests
from typing import List, Dict, Optional


class TemplateItem:
    def __init__(self, keyword: str, title: str, content: str, category: str = "일반"):
        self.keyword = keyword.strip()
        self.title = title.strip()
        self.content = content
        self.category = category.strip()

    def matches(self, query: str) -> bool:
        if not query:
            return True
        terms = query.lower().split()
        search_target = f"{self.keyword} {self.title} {self.category} {self.content}".lower()
        return all(term in search_target for term in terms)


class TemplateManager:
    def __init__(self, config_path: str = "config.json"):
        self.config_path = config_path
        self.config = self.load_config()
        self.templates: List[TemplateItem] = []
        self.load_templates()

    def load_config(self) -> dict:
        default_config = {
            "hotkey": "<alt>+q",
            "csv_file": "chat_templates.csv",
            "remote_csv_url": "",
            "restore_clipboard": True,
            "paste_delay_ms": 70
        }
        if os.path.exists(self.config_path):
            try:
                with open(self.config_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    default_config.update(data)
            except Exception as e:
                print(f"[설정 로드 오류] {e}")
        return default_config

    def save_config(self):
        try:
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(self.config, f, ensure_ascii=False, indent=2)
        except Exception as e:
            print(f"[설정 저장 오류] {e}")

    def load_templates(self) -> int:
        """로컬 CSV 파일에서 템플릿 로드"""
        csv_path = self.config.get("csv_file", "chat_templates.csv")
        if not os.path.isabs(csv_path):
            base_dir = os.path.dirname(os.path.abspath(self.config_path))
            csv_path = os.path.join(base_dir, csv_path)

        if not os.path.exists(csv_path):
            self.templates = []
            return 0

        encodings = ["utf-8-sig", "utf-8", "cp949"]
        items = []

        for enc in encodings:
            try:
                with open(csv_path, mode="r", encoding=enc) as f:
                    reader = csv.DictReader(f)
                    for row in reader:
                        kw = row.get("keyword") or row.get("키워드") or ""
                        title = row.get("title") or row.get("제목") or kw
                        content = row.get("content") or row.get("내용") or ""
                        category = row.get("category") or row.get("카테고리") or "일반"
                        if kw or title or content:
                            items.append(TemplateItem(kw, title, content, category))
                self.templates = items
                print(f"[로드 완료] {len(self.templates)}개 항목 ({enc})")
                return len(self.templates)
            except UnicodeDecodeError:
                continue
            except Exception as e:
                print(f"[CSV 파싱 에러 - {enc}] {e}")
                break

        self.templates = items
        return len(self.templates)

    def sync_from_remote_csv(self) -> (bool, str):
        """구글 시트 웹게시 CSV URL 등 원격 주소에서 다운로드 및 로컬 덮어쓰기"""
        url = self.config.get("remote_csv_url", "").strip()
        if not url:
            return False, "설정된 원격 CSV URL이 없습니다."

        try:
            resp = requests.get(url, timeout=10)
            resp.raise_for_status()
            content = resp.content.decode("utf-8-sig", errors="replace")

            csv_path = self.config.get("csv_file", "chat_templates.csv")
            if not os.path.isabs(csv_path):
                base_dir = os.path.dirname(os.path.abspath(self.config_path))
                csv_path = os.path.join(base_dir, csv_path)

            with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
                f.write(content)

            count = self.load_templates()
            return True, f"동기화 성공! {count}개 템플릿을 불러왔습니다."
        except Exception as e:
            return False, f"동기화 실패: {e}"

    def search(self, query: str) -> List[TemplateItem]:
        if not query:
            return self.templates
        return [item for item in self.templates if item.matches(query)]
