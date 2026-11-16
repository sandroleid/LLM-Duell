; Installer für LLM Duell (Inno Setup 6).
;
;   iscc /DVersion=1.1.0 Windows\installer.iss   →  dist\LLM-Duell-1.1.0-Setup.exe
;
; Erwartet die fertige App unter dist\LLM Duell.exe (siehe build.ps1).
; Installiert ohne Administratorrechte in das Benutzerprofil.

#ifndef Version
  #define Version "1.1.0"
#endif

[Setup]
AppId={{6F0C2A5E-3B1D-4E7A-9C48-5D2E8A1B7F30}
AppName=LLM Duell
AppVersion={#Version}
AppVerName=LLM Duell {#Version}
AppPublisher=Sandro Leidmann
AppPublisherURL=https://github.com/sandroleid/LLM-Duell
AppSupportURL=https://github.com/sandroleid/LLM-Duell/issues
DefaultDirName={autopf}\LLM Duell
DefaultGroupName=LLM Duell
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=LLM-Duell-{#Version}-Setup
SetupIconFile=Resources\AppIcon.ico
UninstallDisplayIcon={app}\LLM Duell.exe
UninstallDisplayName=LLM Duell
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.18362
VersionInfoVersion={#Version}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\LLM Duell.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LLM Duell"; Filename: "{app}\LLM Duell.exe"
Name: "{autodesktop}\LLM Duell"; Filename: "{app}\LLM Duell.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LLM Duell.exe"; Description: "{cm:LaunchProgram,LLM Duell}"; Flags: nowait postinstall skipifsilent
