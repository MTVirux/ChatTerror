#!/bin/bash
# Writes "<job>=true|false" to $GITHUB_OUTPUT for each CI job, depending on which files changed.
set -euo pipefail

declare -A jobs=(
    [unit]='^(src/ChatTerror\.(Protocol|Plugin)/|tests/ChatTerror\.(Protocol|Plugin)\.Tests/|test-vectors/|ChatTerror\.sln$)'
    [integration]='^(src/ChatTerror\.(Server|Protocol)/|src/ChatTerror\.Plugin/Logic/|tests/ChatTerror\.Server\.Tests/)'
    [web]='^(web/|test-vectors/)'
    [docker]='^(Dockerfile$|\.dockerignore$|web/|src/ChatTerror\.(Server|Protocol)/|tests/ChatTerror\.Smoke\.Tests/)'
)

base=""
if [ "$EVENT" = pull_request ]; then
    base=$(git merge-base "origin/$BASE_REF" HEAD)
elif [ "$EVENT" = push ] && [[ "$REF" == refs/heads/* ]]; then
    # The last commit that passed CI, not the previous push, so a failed change keeps being tested until it passes
    # and is never deployed on the back of an unrelated commit.
    base=$(gh api "repos/$REPO/actions/workflows/ci.yml/runs?branch=${REF#refs/heads/}&event=push&status=success&per_page=1" \
        --jq '.workflow_runs[0].head_sha // empty' || true)
fi

run_all=true
changed=""
if [ -n "$base" ] && git merge-base --is-ancestor "$base" HEAD 2>/dev/null; then
    changed=$(git diff --name-only "$base" HEAD)
    grep -q '^\.github/' <<< "$changed" || run_all=false
fi

if $run_all; then
    echo "Running everything"
else
    echo "Changed since $base:"
    echo "$changed"
fi

for job in "${!jobs[@]}"; do
    if $run_all || grep -qE "${jobs[$job]}" <<< "$changed"; then
        echo "$job=true"
    else
        echo "$job=false"
    fi
done | tee -a "$GITHUB_OUTPUT"
