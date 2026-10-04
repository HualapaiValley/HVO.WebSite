"""Check rendered relative documentation navigation, using CommonMark tokens.

Parser extraction adapted from the attributed #416 navigation preparation.
External reachability and historical correctness require content review.
"""
import argparse
from collections import Counter, deque
from html import unescape
from html.parser import HTMLParser
import importlib.metadata
import json
import os
import posixpath
import re
import subprocess
import unicodedata
from urllib.parse import unquote, urlsplit
from markdown_it import MarkdownIt

PARSER = MarkdownIt("commonmark").enable("table")

def git(*args):
    return subprocess.check_output(["git", *args])

class HtmlNavigation(HTMLParser):
    def __init__(self, initial_depth=0):
        super().__init__(convert_charrefs=True)
        self.links, self.ids = [], []
        self.illustration_depth = initial_depth
    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag in {"generated_content", "pre", "code"}:
            self.illustration_depth += 1
        if self.illustration_depth:
            return
        for attr in ("id", "name" if tag == "a" else "id"):
            if values.get(attr) and values[attr] not in self.ids:
                self.ids.append(values[attr])
        for attr in ("href", "src"):
            if values.get(attr):
                self.links.append((values[attr], self.getpos()[0], f"html:{tag}:{attr}"))
    def handle_endtag(self, tag):
        if tag in {"generated_content", "pre", "code"} and self.illustration_depth:
            self.illustration_depth -= 1

def slug(text):
    # ASCII rules match GitHub headings used in this snapshot. Non-ASCII
    # punctuation/emoji remains a documented manual qualification below.
    text = text.lower()
    text = "".join(c for c in text if not (unicodedata.category(c)[0] in "PS" and c not in "-_"))
    return text.replace(" ", "-")

def plain(children):
    result = ""
    for token in children or []:
        if token.type in {"text", "code_inline"}:
            result += token.content
        elif token.type in {"softbreak", "hardbreak"}:
            result += " "
        elif token.type == "image":
            result += token.content
    return result

def walk(children):
    for token in children or []:
        yield token
        yield from walk(token.children)

def locations(lines, start, end, href):
    # Prefer actual destination spelling; reference uses fall back to the
    # containing CommonMark block, recorded as a bounded location range.
    variants = {href, unquote(href), unescape(href)}
    for n in range(start, min(end, len(lines))):
        if any(v and v in lines[n] for v in variants):
            return n + 1, "destination-spelling"
    return start + 1, "containing-block-start"

def parse_doc(path, content):
    env = {}
    tokens = PARSER.parse(content, env)
    links, headings, html_ids, uncertain = [], [], [], []
    duplicate = Counter()
    html_illustration_depth = 0
    lines = content.splitlines()
    for index, token in enumerate(tokens):
        if token.type == "heading_open" and index + 1 < len(tokens):
            inline = tokens[index + 1]
            text = plain(inline.children)
            candidate = slug(text)
            anchor = candidate if duplicate[candidate] == 0 else f"{candidate}-{duplicate[candidate]}"
            while duplicate[anchor] and anchor != candidate:
                duplicate[candidate] += 1
                anchor = f"{candidate}-{duplicate[candidate]}"
            duplicate[candidate] += 1
            if anchor != candidate:
                duplicate[anchor] += 1
            headings.append({"line": token.map[0] + 1, "text": text, "anchor": anchor})
            if any(ord(c) > 127 and unicodedata.category(c)[0] in "PS" for c in text):
                uncertain.append({"line": token.map[0] + 1, "reason": "unicode-symbol/punctuation heading slug requires manual GitHub qualification", "text": text, "provisionalAnchor": anchor})
        if token.type == "inline":
            start, end = token.map or (0, 1)
            for child in walk(token.children):
                # CommonMark treats the underscore custom wrapper as literal
                # text. Qualify only a standalone illustrative wrapper token.
                if child.type == "text" and child.content.strip() == "<generated_content>":
                    html_illustration_depth += 1
                elif child.type == "text" and child.content.strip() == "</generated_content>":
                    html_illustration_depth = max(0, html_illustration_depth - 1)
                if child.type in {"link_open", "image"}:
                    if html_illustration_depth:
                        continue
                    attr = "href" if child.type == "link_open" else "src"
                    href = child.attrGet(attr)
                    if not href:
                        continue
                    line, precision = locations(lines, start, end, href)
                    links.append({"source": path, "line": line, "blockEndLine": end, "precision": precision, "kind": "markdown-link" if attr == "href" else "markdown-image", "href": href})
                if child.type == "html_inline":
                    html = HtmlNavigation(html_illustration_depth)
                    html.feed(child.content)
                    html_illustration_depth = html.illustration_depth
                    html_ids.extend(html.ids)
                    for href, offset, kind in html.links:
                        line, precision = locations(lines, start, end, href)
                        links.append({"source": path, "line": line, "blockEndLine": end, "precision": precision, "kind": kind, "href": href})
        if token.type == "html_block":
            html = HtmlNavigation(html_illustration_depth)
            html.feed(token.content)
            html_illustration_depth = html.illustration_depth
            html_ids.extend(html.ids)
            for href, offset, kind in html.links:
                links.append({"source": path, "line": token.map[0] + offset, "precision": "html-parser-offset", "kind": kind, "href": href})
    references = []
    for label, value in env.get("references", {}).items():
        references.append({"label": label, "href": value["href"], "line": value.get("map", [0])[0] + 1, "parsedDefinition": value})
    codeblocks = [{"line": t.map[0] + 1, "type": t.type, "info": t.info, "navigationLikeCount": len(re.findall(r"\]\(|\bhref=|\bsrc=", t.content))} for t in tokens if t.type in {"fence", "code_block"} and re.search(r"\]\(|\bhref=|\bsrc=", t.content)]
    return {"links": links, "headings": headings, "htmlIds": sorted(set(html_ids)), "referenceDefinitions": references, "ambiguousSlugs": uncertain, "illustrativeCodeBlocks": codeblocks, "unclosedIllustrationDepth": html_illustration_depth}


def inspect(root, ref=None):
    """Read immutable Git blobs or the complete non-ignored working inventory."""
    def run(*args):
        return subprocess.check_output(["git", "-C", str(root), *args])
    tree, bodies, symlinks = {}, {}, {}
    if ref:
        for row in run("ls-tree", "-r", "-z", ref).split(b"\0"):
            if not row:
                continue
            metadata, rawpath = row.split(b"\t", 1)
            mode, kind, blob = metadata.decode().split()
            path = rawpath.decode()
            tree[path] = {"mode": mode, "blob": blob}
            if mode == "120000" or path.lower().endswith(".md"):
                bodies[path] = run("show", f"{ref}:{path}").decode()
    else:
        from pathlib import Path
        paths = set(run("ls-files", "--cached", "--others", "--exclude-standard", "-z").decode().split("\0")) - {""}
        for path in sorted(paths):
            file = Path(root) / path
            if not file.exists() and not file.is_symlink():
                continue  # a deliberate working-tree deletion is absent
            mode = "120000" if file.is_symlink() else "100644"
            tree[path] = {"mode": mode}
            if mode == "120000":
                bodies[path] = os.readlink(file)
            elif path.lower().endswith(".md"):
                bodies[path] = file.read_text()
    directories = {"."}
    for path in tree:
        parts = path.split("/")
        for length in range(1, len(parts)):
            directories.add("/".join(parts[:length]))
        if tree[path]["mode"] == "120000":
            symlinks[path] = bodies[path].strip()

    def resolve(base, destination):
        queue = deque((posixpath.join(base, destination) if not destination.startswith("/") else destination.lstrip("/")).split("/"))
        output, hops = [], 0
        while queue:
            part = queue.popleft()
            if part in {"", "."}:
                continue
            if part == "..":
                if not output:
                    return None, "escapes-repository"
                output.pop()
                continue
            path = "/".join([*output, part])
            if path in symlinks:
                hops += 1
                if hops > 20:
                    return None, "symlink-loop"
                target = symlinks[path]
                if target.startswith("/"):
                    return None, "absolute-symlink"
                queue.extendleft(reversed(target.split("/")))
            else:
                output.append(part)
        return "/".join(output) or ".", None

    docs = {path: parse_doc(path, bodies[path]) for path in tree
            if path.lower().endswith(".md") and tree[path]["mode"] != "120000"}
    links, failures, qualifications = [], [], []
    for source, doc in docs.items():
        if doc["unclosedIllustrationDepth"]:
            failures.append({"source": source, "line": 1, "status": "unclosed-illustration-wrapper", "href": "generated_content/pre/code"})
        for original in doc["links"]:
            row = dict(original)
            url = urlsplit(unescape(row["href"]))
            if url.scheme or url.netloc:
                row["status"] = "external-not-checked"
                links.append(row)
                continue
            target, error = resolve(posixpath.dirname(source), unquote(url.path)) if url.path else (source, None)
            row["target"] = target
            if error or target not in tree and target not in directories:
                row["status"] = error or "missing-relative-target"
                failures.append(row)
            elif url.fragment:
                fragment = unquote(url.fragment)
                row["fragment"] = fragment
                targetdoc = docs.get(target) or docs.get(posixpath.join(target, "README.md"))
                if targetdoc:
                    anchors = {heading["anchor"] for heading in targetdoc["headings"]} | set(targetdoc["htmlIds"])
                    if fragment not in anchors:
                        row["status"] = "missing-anchor"
                        failures.append(row)
                    else:
                        row["status"] = "relative-anchor-resolved"
                        if any(h["provisionalAnchor"] == fragment for h in targetdoc["ambiguousSlugs"]):
                            qualifications.append({**row, "qualification": "Consumed Unicode punctuation/symbol heading: qualify against GitHub rendering"})
                elif re.fullmatch(r"L\d+(?:-L\d+)?", fragment) and target in tree:
                    numbers = [int(value) for value in re.findall(r"\d+", fragment)]
                    if ref:
                        lines = len(run("show", f"{ref}:{target}").splitlines())
                    else:
                        from pathlib import Path
                        lines = len((Path(root) / target).read_bytes().splitlines())
                    if min(numbers) < 1 or max(numbers) > lines or len(numbers) == 2 and numbers[0] > numbers[1]:
                        row["status"] = "source-line-out-of-range"
                        failures.append(row)
                    else:
                        row["status"] = "source-line-range-resolved"
                else:
                    row["status"] = "unsupported-fragment"
                    failures.append(row)
            else:
                row["status"] = "relative-directory-resolved" if target in directories else "relative-file-resolved"
            links.append(row)
    return {
        "source": ref or "working-tree-non-ignored-inventory",
        "parser": {name: importlib.metadata.version(name) for name in ("markdown-it-py", "mdurl")},
        "markdownCount": len(docs), "inventoryCount": len(tree),
        "links": links, "failures": failures, "qualifications": qualifications,
        "symlinks": symlinks,
        "referenceDefinitions": sum(len(doc["referenceDefinitions"]) for doc in docs.values()),
        "illustrativeCodeBlocks": sum(len(doc["illustrativeCodeBlocks"]) for doc in docs.values()),
        "unicodeHeadingCount": sum(len(doc["ambiguousSlugs"]) for doc in docs.values()),
        "qualificationLimits": "External reachability, undefined literal references, historical facts and browser-specific rendering require content review. Fenced/code/generated_content illustrations are not rendered navigation. No whole-file/template exclusions."
    }

def main():
    from pathlib import Path
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--ref", help="Read immutable Git blobs instead of working files")
    parser.add_argument("--json", help="Write the complete inspection report")
    args = parser.parse_args()
    report = inspect(Path(args.root).resolve(), args.ref)
    if args.json:
        Path(args.json).write_text(json.dumps(report, indent=2) + "\n")
    print(f"{report['markdownCount']} Markdown files; {len(report['links'])} destinations; {len(report['failures'])} failures; {len(report['qualifications'])} manual qualifications")
    for row in report["failures"] + report["qualifications"]:
        print(f"{row['source']}:{row['line']}: {row['status']} {row['href']}")
    return 1 if report["failures"] or report["qualifications"] else 0

if __name__ == "__main__":
    raise SystemExit(main())
