SetMetadata("AppName", "TestApp")
SetMetadata("Version", "1.0.0")
SetMetadata("Company", "TestCorp")

SetAppIcon("DeployPaladin.ico")

SetTheme("Windows11")
SetInstallDirSuffix("TestApp")

AddStep("Welcome", { title = "Welcome to TestApp", description = "This wizard will install TestApp on your computer.\n\nClick Next to continue." })
AddStep("License", { title = "License Agreement", description = "Please review the license terms.", contentFile = "LICENSE.txt", requireScroll = true })
AddStep("Folder", { title = "Select Folder", description = "Choose where to install TestApp." })
AddStep("Shortcuts", {title = "Select Optional Tasks", description = "Check the items you would like the installer to perform." })
AddStep("Install", { title = "Installing...", description = "Copying files to your system." })
AddStep("Finish", { title = "Installation Complete", description = "TestApp has been installed successfully.\n\nClick Finish to close." })

MkDir("%INSTALLDIR%")
CopyFiles("TestApp.exe", "%INSTALLDIR%/TestApp.exe")
CopyFiles("config.json", "%INSTALLDIR%/config.json")
CopyFiles("helper.dll", "%INSTALLDIR%/helper.dll")

-- Desktop and Start Menu shortcuts
CreateShortcut("%INSTALLDIR%/TestApp.exe", "%DESKTOP%", "TestApp", { label = "Create Desktop Shortcut", isOptional = true, isSelected = true })
CreateShortcut("%INSTALLDIR%/TestApp.exe", "%STARTMENU%/TestCorp", "TestApp", { label = "Create Start Menu Entry", isOptional = true, isSelected = true })

CheckRegistry("HKCU", "Software\\TestCorp\\TestApp", "InstallDir")
CreateRegistry("HKCU", "Software\\TestCorp\\TestApp", "InstallDir", "%INSTALLDIR%")
CreateRegistry("HKCU", "Software\\TestCorp\\TestApp", "Version", "1.0.0")
