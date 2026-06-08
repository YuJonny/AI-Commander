接线员 Commander - Windows Version (WPF + WPF-UI)
====================================================

Tech stack:
  - WPF (.NET 8)
  - WPF-UI library (Fluent Design 2 controls)
  - H.NotifyIcon.Wpf (system tray)
  - Mica material on Windows 11

Requirements:
  - .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)

Build:
  1. Double-click 一键编译.bat (Chinese) or build.bat
  2. Or manually: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
  3. Output: publish/Commander.exe

Features:
  - Modern Fluent Design 2 UI (Windows 11)
  - Mica material window background
  - Rounded window corners
  - Map headset media play/pause to any keyboard key
  - Default target: Right Alt
  - System tray background running
  - Launch at login (Windows Registry)
  - Chinese/English language switching

Files:
  Business logic:
    - MediaKeyInterceptor.cs - Low-level keyboard hook + SendInput
    - Settings.cs           - JSON config in %APPDATA%/Commander
    - StartupHelper.cs      - Registry-based startup
    - I18n.cs               - Chinese/English strings

  UI (WPF/XAML):
    - App.xaml/.cs          - App entry, single-instance mutex
    - MainWindow.xaml/.cs   - FluentWindow with tabs + tray
    - FunctionPage.xaml/.cs - Main settings page
    - AboutPage.xaml/.cs    - About info
    - LanguagePage.xaml/.cs - Language switcher
