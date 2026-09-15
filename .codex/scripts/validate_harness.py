"""Read-only checks for the bundled Codex instructions; no runtime claims."""

from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit


def validate(root: Path) -> int:
    root = root.resolve()
    bundle = root / ".codex"
    errors = []
    warnings = []
    required = [root / "AGENTS.md", bundle / "README.md", bundle / "PROJECT_GUIDE.md"]
    skills = sorted((bundle / "skills").glob("*/SKILL.md"))
    if not skills:
        errors.append("No bundled SKILL.md files found")
    documents = sorted(set(required + list(bundle.rglob("*.md"))))
    local_only = {"Docs/ACTIVE_CONTEXT.md", "Docs/RECENT_CHANGES.md"}
    for document in documents:
        if not document.is_file():
            errors.append(f"Missing required file: {document.relative_to(root)}")
            continue
        content = document.read_text(encoding="utf-8")
        for target in re.findall(r"\[[^\]\n]+\]\(([^)\n]+)\)", content):
            target = target.strip().strip("<>")
            url = urlsplit(target)
            if url.scheme in {"https", "http"} or not url.path:
                continue
            destination = (document.parent / unquote(url.path)).resolve()
            if not destination.is_relative_to(root):
                errors.append(f"Link escapes repository: {document.relative_to(root)} -> {target}")
            elif not destination.exists():
                relative = destination.relative_to(root).as_posix()
                message = f"Missing link: {document.relative_to(root)} -> {target}"
                (warnings if relative in local_only else errors).append(message)
        if document in skills:
            frontmatter = re.match(r"\A---\n(.*?)\n---\n", content, re.DOTALL)
            if not frontmatter:
                errors.append(f"Missing skill frontmatter: {document.relative_to(root)}")
                continue
            fields = dict(re.findall(r"^(name|description):\s*(.+)$", frontmatter[1], re.MULTILINE))
            name = fields.get("name", "")
            if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", name) or len(name) > 64:
                errors.append(f"Invalid skill name: {document.relative_to(root)}")
            if name != document.parent.name or not fields.get("description", "").strip():
                errors.append(f"Skill folder/name mismatch or missing description: {document.relative_to(root)}")
    for message in warnings:
        print(f"WARN: {message} (optional local continuity file)")
    for message in errors:
        print(f"ERROR: {message}")
    print(f"Checked {len(documents)} documents and {len(skills)} skills; {len(errors)} errors, {len(warnings)} warnings.")
    print("Checks cover file links and basic metadata only; not native discovery, hooks, MCP, or Unity behavior.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(validate(Path(__file__).resolve().parents[2]))
