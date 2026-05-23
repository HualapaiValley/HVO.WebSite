#!/usr/bin/env bash
set -euo pipefail

missing=0

while IFS= read -r file; do
	if ! grep -q 'TestCategory("Integration")' "$file"; then
		printf 'Missing [TestCategory("Integration")]: %s\n' "$file" >&2
		missing=1
	fi
done < <(find tests -path '*/Integration/*.cs' -type f | sort)

while IFS= read -r file; do
	if ! grep -q 'TestCategory("Live")' "$file"; then
		printf 'Missing [TestCategory("Live")]: %s\n' "$file" >&2
		missing=1
	fi
done < <(find tests -path '*/Live/*.cs' -type f | sort)

exit "$missing"
