"""Check inline specialist terms on the existing synthetic local preview.

Start the preview at port 5210 with clinimark_preview_20261022 first. Uses the existing preview account; creates no
accounts, changes no filters, and submits no annotations. This checks rendered HTML only.
"""
from html.parser import HTMLParser
import http.cookiejar
import json
from pathlib import Path
import urllib.parse
import urllib.request


BASE = "http://127.0.0.1:5210"
EMAIL = "preview-specialist@example.net"
PASSWORD = "SyntheticOnly!2026"  # Existing disposable preview account only.
SOURCE_DESCRIPTION = "Synthetic image: lesion and boundary. Not patient data."
DIAGNOSIS = "Diagnosis: Synthetic lesion"
VOID_ELEMENTS = {"area", "base", "br", "col", "embed", "hr", "img", "input",
                 "link", "meta", "param", "source", "track", "wbr"}


class Element:
    def __init__(self, tag, attrs=()):
        self.tag = tag
        self.attrs = dict(attrs)
        self.children = []

    def elements(self):
        for child in self.children:
            if isinstance(child, Element):
                yield child
                yield from child.elements()

    def text(self):
        return "".join(child.text() if isinstance(child, Element) else child
                       for child in self.children)

    def has_class(self, name):
        return name in self.attrs.get("class", "").split()


class Document(HTMLParser):
    def __init__(self, source):
        super().__init__(convert_charrefs=True)
        self.root = Element("document")
        self.stack = [self.root]
        self.feed(source)
        self.close()

    def handle_starttag(self, tag, attrs):
        node = Element(tag, attrs)
        self.stack[-1].children.append(node)
        if tag not in VOID_ELEMENTS:
            self.stack.append(node)

    def handle_endtag(self, tag):
        for index in range(len(self.stack) - 1, 0, -1):
            if self.stack[index].tag == tag:
                del self.stack[index:]
                break

    def handle_data(self, data):
        self.stack[-1].children.append(data)


class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.token = None

    def request(self, route, form=None):
        data = None
        if form is not None:
            assert self.token, "Missing anti-forgery token before form submission"
            data = urllib.parse.urlencode(
                dict(form, __RequestVerificationToken=self.token)).encode()
        with self.opener.open(BASE + route, data=data, timeout=15) as response:
            source = response.read().decode("utf-8")
            assert response.status == 200, (response.status, response.geturl())
            document = Document(source)
            for element in document.root.elements():
                if element.tag == "input" and element.attrs.get("name") == "__RequestVerificationToken":
                    self.token = element.attrs.get("value")
            return urllib.parse.urlparse(response.geturl()).path, document


def main():
    client = Client()
    client.request("/Identity/Login")
    route, document = client.request("/Identity/PostLogin", {"Email": EMAIL, "Password": PASSWORD})
    assert route == "/Home/Professional", "Existing preview account did not reach specialist view"

    elements = list(document.root.elements())
    descriptions = [element for element in elements if element.has_class("header-text")]
    assert len(descriptions) == 1, "Expected one image description"
    description = descriptions[0]
    paragraphs = [element for element in description.elements() if element.tag == "p"]
    assert len(paragraphs) == 1, "Expected one description paragraph"
    paragraph = paragraphs[0]
    text = paragraph.text()
    assert text.strip() == SOURCE_DESCRIPTION + " " + DIAGNOSIS, "Preview fixture or description text changed"
    terms = [element for element in elements if element.has_class("keyword")]
    inline_terms = [element for element in paragraph.elements() if element.has_class("keyword")]
    assert terms == inline_terms, "Every term must appear inside the description paragraph"
    assert [term.attrs.get("data-keyword") for term in terms] == ["lesion", "boundary"], "Expected canonical terms in description order"
    assert description.attrs.get("data-keyword-count") == "2", "Description must declare the complete keyword count"
    assert not any(element.has_class("annotation-terms") for element in elements), "Separate keyword strip must be absent"
    assert all(element.tag == "span" for element in paragraph.elements()), "Unexpected generated HTML inside plain description"

    offset = 0
    positions = {}

    def collect_positions(element):
        nonlocal offset
        if element.has_class("keyword"):
            positions[element.attrs["data-keyword"]] = offset
        for child in element.children:
            if isinstance(child, Element):
                collect_positions(child)
            else:
                offset += len(child)

    collect_positions(paragraph)
    diagnosis_start = text.index(DIAGNOSIS)
    for term in terms:
        canonical = term.attrs["data-keyword"]
        assert term.tag == "span" and term.attrs.get("role") == "button", "Inline terms must retain button semantics"
        assert term.attrs.get("tabindex") == "0", "Inline terms must be keyboard reachable"
        assert term.text() == canonical, "Displayed source phrase must preserve its source text"
        assert positions[canonical] == text.lower().index(canonical.lower()), "Only the first source occurrence should be highlighted"
        assert positions[canonical] + len(term.text()) <= diagnosis_start, "Diagnosis must not supply annotation terms"

    report = dict(passed=True, url=BASE + route, account=EMAIL,
                  description=SOURCE_DESCRIPTION, keywords=["lesion", "boundary"],
                  inlineSourceOccurrences=True, separateKeywordStrip=False,
                  diagnosisSuffix=DIAGNOSIS, diagnosisUsedAsKeyword=False,
                  knownFixtureTextPreserved=True, unexpectedDescriptionTags=False,
                  annotationSubmissions=0, accountCreations=0)
    output = Path(__file__).resolve().parents[1] / ".artifacts" / "inline-keywords-check.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
