#!/bin/bash
# SessionStart hook: install the .NET SDK (and the local Fable tool + JS test deps)
# so `dotnet build`/`dotnet test` and the Fable->vitest harness work in
# Claude Code on the web. Idempotent and non-interactive.
set -euo pipefail

# Only run in the remote (web) environment; local machines already have a toolchain.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
DOTNET_DIR="$HOME/.dotnet"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# 1. Install the .NET 10 SDK (the test projects target net10.0). Skip if already present.
if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Installing .NET 10 SDK into $DOTNET_DIR ..."
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
else
  echo ".NET 10 SDK already installed."
fi

export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$PATH"

# Persist the toolchain on PATH for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
    echo "export PATH=\"$DOTNET_DIR:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$CLAUDE_ENV_FILE"
fi

# 2. Restore the local Fable tool (manifest lives at Test/dotnet-tools.json).
dotnet tool restore --tool-manifest "$PROJECT_DIR/Test/dotnet-tools.json"

# 3. Install the JS deps for the vitest harness (npm's preinstall re-runs tool restore).
if [ -f "$PROJECT_DIR/Test/package.json" ]; then
  ( cd "$PROJECT_DIR/Test" && npm install )
fi

echo "SessionStart hook complete: $(dotnet --version) SDK ready."
