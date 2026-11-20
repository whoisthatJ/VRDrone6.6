"""The documentation in three languages must stay the same document: same pages, same code, same tables, live links."""
import re
from pathlib import Path

import pytest

DOCS = Path(__file__).resolve().parents[3] / "Docs" / "bench"
LANGS = ["ru", "en", "kk"]
PAGES = ["README", "quickstart", "rules", "drone", "sdk", "tracks", "dataset", "docker", "ros2", "troubleshooting",
         "organizer", "protocol"]

pytestmark = pytest.mark.skipif(not DOCS.is_dir(), reason="the documentation is not next to this checkout")


def text(lang: str, page: str) -> str:
    return (DOCS / lang / f"{page}.md").read_text(encoding="utf-8")


def code_blocks(s: str):
    return re.findall(r"```[^\n]*\n(.*?)```", s, flags=re.S)


def strip_comments(block: str) -> str:
    """Code without its comments and blank padding: comments are translated, code is not."""
    lines = []
    for line in block.splitlines():
        line = re.sub(r"\s+#\s.*$", "", line)          # trailing comment
        if re.match(r"^\s*#", line):
            continue
        line = re.sub(r"<[^<>|]*>", "<>", line)       # placeholders such as <attempt folder> are translated too
        lines.append(re.sub(r"\s+", " ", line).strip())
    return "\n".join(x for x in lines if x)


def rows(s: str) -> int:
    return sum(1 for line in s.splitlines() if line.startswith("|"))


def heads(s: str):
    return [len(m) for m in re.findall(r"^(#+) ", re.sub(r"```.*?```", "", s, flags=re.S), flags=re.M)]


@pytest.mark.parametrize("lang", LANGS)
def test_every_page_exists(lang):
    assert sorted(p.stem for p in (DOCS / lang).glob("*.md")) == sorted(PAGES)


@pytest.mark.parametrize("lang", LANGS)
@pytest.mark.parametrize("page", PAGES)
def test_links_resolve(lang, page):
    for target in re.findall(r"\]\(([^)#]+?)(?:#[^)]*)?\)", text(lang, page)):
        if "://" in target:
            continue
        assert (DOCS / lang / target).is_file(), f"{lang}/{page}.md links to {target}"


@pytest.mark.parametrize("lang", ["en", "kk"])
@pytest.mark.parametrize("page", PAGES)
def test_same_structure_as_russian(lang, page):
    ru, other = text("ru", page), text(lang, page)
    assert [strip_comments(b) for b in code_blocks(other)] == [strip_comments(b) for b in code_blocks(ru)], "code blocks differ"
    assert rows(other) == rows(ru), "tables differ in size"
    assert heads(other) == heads(ru), "headings differ"


@pytest.mark.parametrize("page", PAGES)
def test_kazakh_pages_say_they_need_review(page):
    assert "ана тілді маманның тексеруін қажет етеді" in text("kk", page)
