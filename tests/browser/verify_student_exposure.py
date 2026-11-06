"""Check the existing synthetic student's HTML without submitting annotations.

Start the preview at port 5210. The existing preview-student account must have an available synthetic image. Sign-in can
reserve that image; this script creates no accounts and writes no annotations.
"""
import json
from pathlib import Path
import re

from verify_inline_keywords import Client, PASSWORD, SOURCE_DESCRIPTION


def main():
    client = Client()
    client.request("/Identity/Login")
    route, document = client.request("/Identity/PostLogin", {
        "Email": "preview-student@example.net", "Password": PASSWORD})
    assert route == "/Home/Student", "Existing student account did not reach student view"

    elements = list(document.root.elements())
    assert not any(element.has_class("header-text") for element in elements), "Students must not see the specialist description"
    assert not any(element.has_class("keyword") for element in elements), "Students must label their own groups"
    assert SOURCE_DESCRIPTION not in document.root.text(), "Source description leaked into student HTML"
    metadata = [element for element in elements if element.has_class("metadata-item")]
    assert any("Diagnosis:" in element.text() and "Synthetic lesion" in element.text()
               for element in metadata), "Student diagnosis metadata is missing"
    assert any(element.attrs.get("id") == "annotations-list" for element in elements), "Student group editor is missing"
    assert any(element.attrs.get("id") == "submit-button" for element in elements), "Student submit action is missing"

    script = next(element.text() for element in elements
                  if element.tag == "script" and "window.annotatedMedData =" in element.text())
    match = re.search(r"window\.annotatedMedData =\s*(.+);", script)
    assert match, "Missing student image identity payload"
    payload = json.loads(match.group(1))
    assert set(payload) == {"id", "imageUrl"}, "Student payload exposes unnecessary research fields"
    assert isinstance(payload["id"], int) and payload["id"] > 0, "Missing image ID"
    assert payload["imageUrl"], "Missing image URL"

    report = dict(passed=True, url="http://127.0.0.1:5210" + route,
                  account="preview-student@example.net", diagnosisVisible=True,
                  specialistDescriptionVisible=False, prescribedKeywordsVisible=False,
                  imagePayloadFields=sorted(payload), ownGroupLabels=True,
                  annotationSubmissions=0, accountCreations=0)
    output = Path(__file__).resolve().parents[1] / ".artifacts" / "student-exposure-check.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
