#!/usr/bin/env bash
# spec-loop.sh: build spec steps automatically.
#   1. A free OpenCode model implements one step.
#   2. The script runs dotnet build + test locally (free). Failures go back to the builder.
#   3. Claude Code reviews the staged diff with read-only tools. FAIL feedback goes back to the builder.
#   4. On PASS the script commits and moves to the next step.
# Stops and hands back to you if a step fails MAX_ATTEMPTS times.
#
# Usage:  scripts/spec-loop.sh <spec-file> <first-step> <last-step>
# Example: scripts/spec-loop.sh docs/specs/002-ui-shell.md 2 4
# Env:    BUILDER_MODEL  (default opencode/big-pickle; any provider/model from `opencode models`)
#         REVIEWER_MODEL (default sonnet)
#         MAX_ATTEMPTS   (default 3)

set -uo pipefail

SPEC="${1:?usage: $0 <spec-file> <first-step> <last-step>}"
FIRST="${2:?first step number required}"
LAST="${3:?last step number required}"
BUILDER_MODEL="${BUILDER_MODEL:-opencode/big-pickle}"
REVIEWER_MODEL="${REVIEWER_MODEL:-sonnet}"
MAX_ATTEMPTS="${MAX_ATTEMPTS:-3}"

ROOT="$(git rev-parse --show-toplevel)" || exit 1
cd "$ROOT"
[ -f "$SPEC" ] || { echo "Spec not found: $SPEC"; exit 1; }

SPEC_ID="$(basename "$SPEC" .md | cut -d- -f1)"
LOG_DIR="$ROOT/.loop/spec-$SPEC_ID"
mkdir -p "$LOG_DIR"

log() { printf '\n[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }

if [ -n "$(git status --porcelain)" ]; then
  echo "Working tree is not clean. Commit or stash your changes first."; exit 1
fi

BRANCH="spec-$SPEC_ID"
if [ "$(git branch --show-current)" != "$BRANCH" ]; then
  if git show-ref --verify --quiet "refs/heads/$BRANCH"; then git switch -q "$BRANCH"; else git switch -q -c "$BRANCH"; fi
fi
log "Working on branch $BRANCH (builder: $BUILDER_MODEL, reviewer: $REVIEWER_MODEL)"

for step in $(seq "$FIRST" "$LAST"); do
  feedback=""
  passed=0
  for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
    tag="step$step-try$attempt"
    start_head="$(git rev-parse HEAD)"
    log "Step $step, attempt $attempt: building"

    prompt="Read AGENTS.md, then implement Step $step of $SPEC. Only do Step $step. Do NOT run git commit; an automated script commits after review. Make sure 'dotnet build' and 'dotnet test' pass before you finish."
    if [ -n "$feedback" ]; then
      prompt="$prompt

Your previous attempt at this step was rejected. The working tree still contains that attempt. Fix these problems:

$feedback"
    fi

    opencode run --auto -m "$BUILDER_MODEL" "$prompt" > "$LOG_DIR/$tag-builder.log" 2>&1

    # If the builder committed anyway, undo the commit but keep its changes.
    if [ "$(git rev-parse HEAD)" != "$start_head" ]; then
      git reset -q --soft "$start_head"
    fi

    # Gate 1: local build and tests (costs nothing)
    log "Running build and tests"
    if ! { dotnet build --nologo && dotnet test --nologo; } > "$LOG_DIR/$tag-build.log" 2>&1; then
      log "Build or tests failed; sending errors back to the builder"
      feedback="Build or tests failed. Last 60 lines of output:
$(tail -n 60 "$LOG_DIR/$tag-build.log")"
      continue
    fi

    git add -A
    if git diff --cached --quiet; then
      log "Builder made no changes"
      feedback="You made no changes. Implement Step $step."
      continue
    fi

    # Gate 2: Claude review with read-only tools
    log "Reviewing with Claude"
    review_prompt="You are reviewing staged, uncommitted changes for Step $step of $SPEC. Run 'git diff --cached' to see them, and read files as needed. Check the changes against that step of the spec and the non-negotiable rules in AGENTS.md (read-only Graph permissions, no secrets, no network calls in tests, no scope creep). Ignore minor style issues. List any concrete problems that must be fixed. End your reply with exactly one final line: VERDICT: PASS or VERDICT: FAIL"
    claude -p "$review_prompt" \
      --model "$REVIEWER_MODEL" \
      --allowedTools "Read" "Grep" "Glob" "Bash(git diff:*)" "Bash(git status:*)" "Bash(git log:*)" \
      --disallowedTools "Edit" "Write" \
      > "$LOG_DIR/$tag-review.md" 2>&1

    if tail -n 3 "$LOG_DIR/$tag-review.md" | grep -q "VERDICT: PASS"; then
      git commit -q -m "spec-$SPEC_ID step $step: automated loop (attempt $attempt, builder $BUILDER_MODEL)"
      log "Step $step passed review and was committed"
      passed=1
      break
    fi

    log "Review failed; details in $LOG_DIR/$tag-review.md"
    feedback="$(cat "$LOG_DIR/$tag-review.md")"
    git reset -q   # unstage but keep the work for the next attempt
  done

  if [ "$passed" -ne 1 ]; then
    log "Step $step failed $MAX_ATTEMPTS times. Stopping so you can take a look."
    log "The last attempt is left uncommitted in the working tree. Logs: $LOG_DIR"
    exit 2
  fi
done

log "Done: steps $FIRST to $LAST committed on branch $BRANCH."
log "Check them with 'git log main..$BRANCH', then merge into main when you're happy."
