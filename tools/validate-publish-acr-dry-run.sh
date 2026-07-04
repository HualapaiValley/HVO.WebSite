#!/usr/bin/env bash
set -euo pipefail

exec bash tools/validate-publish-dry-run.sh "$@"
