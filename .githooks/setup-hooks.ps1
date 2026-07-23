Write-Host "Activating Git hooks for JsonRepair.NET..." -ForegroundColor Cyan
git config core.hooksPath .githooks
Write-Host "✓ Git hooks activated! Pre-commit checks will now run on 'git commit'." -ForegroundColor Green
