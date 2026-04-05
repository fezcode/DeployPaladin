# Deploy Paladin Use Case Scenario: Distributing "Starfighter Pro"

Imagine you have just finished developing a new PC game called **Starfighter Pro**. You have a folder full of files (`Game.exe`, `engine.dll`, `assets/`, `config.json`) and you want to give players a professional installation experience.

## 1. The Developer Workflow (Creating the Installer)

**Step 1: Build the Projects**

First, build both projects from the solution root:

```powershell
dotnet build DeployPaladin.sln -c Release
```

Then publish the installer UI as a self-contained executable (this is your "base installer"):

```powershell
dotnet publish DeployPaladin.csproj -c Release -r win-x64 --self-contained -o .\publish
```

This produces `publish\DeployPaladin.exe` — the base installer engine.

**Step 2: Prepare your Payload Folder**

Create a staging folder on your computer (e.g., `D:\Releases\StarfighterInstaller`). Inside this folder, place everything the game needs to run, plus a `LICENSE.txt` file for the legal terms.

```
D:\Releases\StarfighterInstaller\
├── installer.lua
├── LICENSE.txt
├── icon.ico
├── Game.exe
├── engine.dll
├── config.json
└── assets/
    ├── textures/
    └── sounds/
```

**Step 3: Write the Lua Script**

In that same staging folder, create an `installer.lua` file to tell Deploy Paladin exactly what to do:

```lua
SetMetadata("AppName", "Starfighter Pro")
SetMetadata("Version", "2.1.0")
SetMetadata("Company", "Workhammer Studios")

-- Choose the theme: "Windows11" or "Aqua"
SetTheme("Windows11")
SetInstallDirSuffix("StarfighterPro")
SetAppIcon("icon.ico")

-- Define the Wizard UI Steps
AddStep("Welcome", { title = "Welcome to Starfighter Pro", description = "Get ready to save the galaxy. \n\nClick Next to continue." })
AddStep("License", { title = "EULA", description = "Please review the license terms.", contentFile = "LICENSE.txt" })
AddStep("Folder", { title = "Choose Location", description = "Select where to install the game files." })
AddStep("Shortcuts", { title = "Shortcuts", description = "Choose which shortcuts to create." })
AddStep("Install", { title = "Installing...", description = "Deploying warp drives..." })
AddStep("Finish", { title = "Ready to Play!", description = "Starfighter Pro has been successfully installed." })

-- Define what happens under the hood during the "Install" step
MkDir("%INSTALLDIR%")
CopyFiles("Game.exe", "%INSTALLDIR%/Game.exe")
CopyFiles("engine.dll", "%INSTALLDIR%/engine.dll")
CopyFiles("config.json", "%INSTALLDIR%/config.json")
CopyDir("assets", "%INSTALLDIR%/assets")

-- Shortcuts (optional ones appear as checkboxes on the Shortcuts step)
CreateShortcut("%INSTALLDIR%/Game.exe", "%DESKTOP%", "Starfighter Pro",
    { label = "Create Desktop Shortcut", isOptional = true, isSelected = true, icon = "%INSTALLDIR%/icon.ico" })
CreateShortcut("%INSTALLDIR%/Game.exe", "%STARTMENU%", "Starfighter Pro",
    { label = "Create Start Menu Shortcut", isOptional = true, isSelected = true })

-- Check if already installed
CheckRegistry("HKCU", "Software\\Workhammer\\Starfighter", "InstallDir")

-- Create a registry key so the system knows it's installed
CreateRegistry("HKCU", "Software\\Workhammer\\Starfighter", "InstallDir", "%INSTALLDIR%")
```

**Step 4: Build the Bundle**

Use the **Deploy Paladin Builder** CLI to fuse your payload folder, the Lua script, and the base installer engine into one distributable executable.

```powershell
DeployPaladin.Builder.exe --payload "D:\Releases\StarfighterInstaller" --base ".\DeployPaladin.exe" --output "D:\Outputs\Starfighter_Setup.exe"
```

Or from the repo with the SDK:

```powershell
dotnet run --project DeployPaladin.Builder -- -p "D:\Releases\StarfighterInstaller" -b ".\publish\DeployPaladin.exe" -o "D:\Outputs\Starfighter_Setup.exe"
```

The builder zips your game files and the Lua script, then appends them to the end of `Starfighter_Setup.exe`.

## 2. The End-User Workflow (Installing the App)

You upload `Starfighter_Setup.exe` to your website. A player downloads it and double-clicks it.

1. **Initialization:** The executable launches. The `BundleReader` checks its own file size, finds the hidden ZIP payload at the end of the `.exe`, and silently loads it into memory. The custom icon (`icon.ico`) is applied to the window.
2. **The Welcome Screen:** It reads the embedded `installer.lua` script, applies the "Windows 11" theme, and shows the Welcome text you wrote.
3. **The License Screen:** The user clicks Next. The engine looks inside the hidden zip file, extracts the text from `LICENSE.txt`, and displays it in a scrollable box.
4. **The Folder Screen:** The user is asked where to install it. It defaults to `C:\Program Files\StarfighterPro` (because of your suffix), but they can hit "Browse..." to change it to `D:\Games\StarfighterPro`.
5. **The Shortcuts Screen:** The user sees checkboxes for "Create Desktop Shortcut" and "Create Start Menu Shortcut". They can toggle each one on or off.
6. **The Install Screen:** The user clicks Next. The progress bar animates while the engine extracts `Game.exe`, `engine.dll`, `config.json`, and the entire `assets/` directory from the internal zip directly onto the user's hard drive at their chosen location. It creates the selected shortcuts and writes to the Windows Registry.
7. **The Finish Screen:** The "Next" button turns into "Finish". The player clicks it, the installer closes, and they are ready to play!

## 3. Re-running the Installer

If the user runs the setup again, the installer detects the existing registry key and offers two choices:

- **Reinstall** — proceeds through the wizard again, overwriting existing files.
- **Uninstall** — deletes the installed files and removes the registry entries.
