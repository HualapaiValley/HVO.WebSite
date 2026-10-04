# Documentation navigation and preservation

The root [index](../README.md) groups current owners. Every active solution
project has a useful linked owner; test projects may share named sections in
[tests/README](../../tests/README.md). Keep one root changelog and decision-oriented
history. Archives retain original source/blob, actual fact date or unknown,
superseded status, successor and related issue. Rebase rendered relative links
when moving content; historical commands remain labeled historical.

## Local guard

The development-only [guard](../../tools/docs-navigation.py) uses CommonMark tokens
rather than regex alone so reference links, images, HTML and fenced examples have
their actual rendering meaning. Its pinned markdown-it-py/mdurl wheels are
hash-verified by a dedicated requirement file; this adds no .NET/runtime dependency
or application compatibility change. Python 3 with venv/pip is required.

From the repository root, keep the virtual environment outside the tracked tree:

```bash
docs_venv="$HOME/.local/state/hvo-docs-navigation/venv"
python3 -m venv "$docs_venv"
"$docs_venv/bin/python" -m pip install --require-hashes -r tools/docs-navigation-requirements.txt
"$docs_venv/bin/python" -m unittest discover -s tools -p docs_navigation_test.py
"$docs_venv/bin/python" tools/docs-navigation.py
"$docs_venv/bin/python" tools/docs-navigation.py --ref HEAD --json /tmp/hvo-docs-navigation.json
```

Working mode reads tracked plus nonignored new files, excluding deliberate working
deletions; immutable mode reads all Git blobs at the selected ref. Both inspect
every Markdown path, including hidden procedure/adapters and marked archives.
The report checks relative files/directories/images/reference links, HTML href/src,
custom IDs, URL-decoded paths/fragments, GitHub duplicate heading anchors, directory
README fragments and source-line ranges. Relative symlinks resolve with loop and
repository-escape guards.

## Qualification limits

External URL reachability and the truth of historical/source claims need content
review. Undefined reference syntax is literal text under CommonMark, not a resolved
link. Fenced code, inline code and explicitly wrapped generated_content are
illustrations; archive body text is otherwise fully checked. No whole file,
directory or manual-template exemption exists. The old template's four nonexistent
sample destinations became a fenced filename example.

The slug rule is qualified by ASCII/GitHub duplicate-heading fixtures. Consumed
Unicode symbol/punctuation headings cause a visible manual qualification and a
nonzero result; unconsumed headings are reported without claiming browser-renderer
equivalence. An external permalink to deleted historical code must name an
ancestor where that file actually exists.

Bounded draft preflight runs the parser fixtures and complete navigation check;
this is a documentation regression check, not independent review, application
validation or admission to standard CI. Immutable-base review/selection policy
and the ordinary reviewed build/test pipeline remain authoritative.

## Compatibility redirects

Six former gateway placeholders have zero incoming links in the pre-#416 rendered
and literal repository audit; unknown external bookmarks justify retaining thin
redirects. Govee points to current HA ownership first. The other five point to
precise future candidate sections. Their retention and final consumers are recorded
in the [84-file ledger](../archive/2026-10-04-documentation-dispositions.md).
