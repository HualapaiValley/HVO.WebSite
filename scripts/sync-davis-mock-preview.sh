#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
container_name="davis-mock-preview"
mockups_dir="$repo_root/src/HVO.Hardware.DavisVantagePro2/wwwroot/mockups"
fonts_dir="$repo_root/src/HVO.WebSite.Themes/wwwroot/fonts"

docker compose -f "$repo_root/docker-compose.design.yml" up -d >/dev/null
docker exec "$container_name" sh -lc 'rm -rf /usr/share/nginx/html/*'
docker cp "$mockups_dir/." "$container_name:/usr/share/nginx/html"
docker exec "$container_name" sh -lc 'mkdir -p /usr/share/nginx/html/HVO.WebSite.Themes/wwwroot/fonts'
docker cp "$fonts_dir/." "$container_name:/usr/share/nginx/html/HVO.WebSite.Themes/wwwroot/fonts"

printf 'Davis mock preview synced to http://127.0.0.1:8091\n'