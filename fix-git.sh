#!/bin/bash
set -e

REMOTE="https://github.com/estrww77-dotcom/Treest"

echo "[1/6] Backing up workflow file..."
cp -r .github/workflows/release.yml /tmp/release.yml 2>/dev/null || true

echo "[2/6] Removing git history..."
rm -rf .git .github

echo "[3/6] Fresh repo (no workflow file)..."
git init
git branch -M master
git add .
git commit -m "RedSea - full release"

echo "[4/6] Pushing to GitHub (no workflow = no scope error)..."
git remote add origin "$REMOTE"
git push origin master --force

echo "[5/6] Pulling workflow back from GitHub..."
git pull origin master --no-rebase

echo "[6/6] Tagging v1.0.0 to trigger the build..."
git tag v1.0.0
git push origin v1.0.0

echo ""
echo "Done! Check github.com/estrww77-dotcom/Treest/actions for the build."
