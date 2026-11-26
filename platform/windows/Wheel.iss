; Windows installer for DeskWheel (Inno Setup 6: https://jrsoftware.org/isinfo.php, or: winget install JRSoftware.InnoSetup).
; Built by build.ps1 -Installer. Installs for the current user only (no administrator prompt),
; adds a Start menu entry, and can start DeskWheel at sign-in. Uninstalling removes all of it.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif

[Setup]
AppId={{6F1C2B7E-5A4D-4E2B-9C1F-8D3A7B2E9F10}
AppName=Desk Wheel
AppVersion={#AppVersion}
AppPublisher=irfanaslam.io
AppPublisherURL=https://irfanaslam.io
AppSupportURL=mailto:hello@irfanaslam.io
AppContact=hello@irfanaslam.io
AppComments=Drop it. Turn it. Done.
DefaultDirName={localappdata}\Programs\DeskWheel
DefaultGroupName=DeskWheel
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\dist
OutputBaseFilename=DeskWheel-{#AppVersion}-{#Runtime}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DeskWheel.exe
SetupIconFile=..\icons\AppIcon.ico
; Shown before installing: free for individuals and small businesses.
LicenseFile=..\..\LICENSE.md
CloseApplications=yes
; Tells File Explorer about the "Open with" entries below.
ChangesAssociations=yes
#ifdef Sign
; Signs Setup.exe, the helper it runs from the temporary folder (setup.tmp) and the uninstaller, so
; Smart App Control and Attack Surface Reduction rules don't block them. build.ps1 passes the command.
SignTool=deskwheel
SignedUninstaller=yes
#endif
; Installs under the new name even when updating an install from when the app was called "Wheel".
UsePreviousAppDir=no
UsePreviousGroup=no
#if Runtime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "startup"; Description: "Start Desk Wheel when I sign in"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\..\dist\DeskWheel-{#Runtime}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[InstallDelete]
; Leftovers from when the app was called "Wheel" (same AppId, so this install replaces it).
Type: filesandordirs; Name: "{localappdata}\Programs\Wheel"
Type: files; Name: "{userprograms}\Wheel.lnk"
Type: files; Name: "{userdesktop}\Wheel.lnk"

[Icons]
Name: "{group}\Desk Wheel"; Filename: "{app}\DeskWheel.exe"
Name: "{userdesktop}\Desk Wheel"; Filename: "{app}\DeskWheel.exe"; Tasks: desktopicon

[Registry]
; Same value DeskWheel's own "Start at login" switch uses, so the two agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskWheel"; \
  ValueData: """{app}\DeskWheel.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DeskWheel"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Wheel"; ValueType: none; Flags: deletevalue

; Picture viewer: "Open with › Desk Wheel" for pictures, and listed under Settings › Apps › Default apps.
; Nothing is taken over: people choose Desk Wheel there if they want it as their viewer.
Root: HKCU; Subkey: "Software\Classes\DeskWheel.Picture"; ValueType: string; ValueData: "Picture"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DeskWheel.Picture"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Picture"
Root: HKCU; Subkey: "Software\Classes\DeskWheel.Picture\DefaultIcon"; ValueType: string; ValueData: """{app}\DeskWheel.exe"",0"
Root: HKCU; Subkey: "Software\Classes\DeskWheel.Picture\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Desk Wheel"
Root: HKCU; Subkey: "Software\Classes\DeskWheel.Picture\shell\open\command"; ValueType: string; ValueData: """{app}\DeskWheel.exe"" --view ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\DeskWheel.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Desk Wheel"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\DeskWheel.exe\shell\open\command"; ValueType: string; ValueData: """{app}\DeskWheel.exe"" --view ""%1"""
Root: HKCU; Subkey: "Software\DeskWheel\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Desk Wheel"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\DeskWheel\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Picture viewer with Next and Back, and offline file tools."
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Desk Wheel"; ValueData: "Software\DeskWheel\Capabilities"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\.jpg\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.jpeg\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.png\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.gif\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".gif"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.bmp\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.webp\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.heic\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".heic"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".heic"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.heif\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".heif"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".heif"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.avif\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".avif"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".avif"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.tif\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".tif"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.tiff\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".tiff"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.ico\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".ico"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".ico"; ValueData: "DeskWheel.Picture"
Root: HKCU; Subkey: "Software\\Classes\\.jfif\\OpenWithProgids"; ValueType: string; ValueName: "DeskWheel.Picture"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\\Classes\\Applications\\DeskWheel.exe\\SupportedTypes"; ValueType: string; ValueName: ".jfif"; ValueData: ""
Root: HKCU; Subkey: "Software\\DeskWheel\\Capabilities\\FileAssociations"; ValueType: string; ValueName: ".jfif"; ValueData: "DeskWheel.Picture"

[Run]
Filename: "{app}\DeskWheel.exe"; Description: "Start Desk Wheel now"; Flags: nowait postinstall skipifsilent
; After an in-app update (a silent install), start the new version again.
Filename: "{app}\DeskWheel.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM DeskWheel.exe /F"; Flags: runhidden; RunOnceId: "StopWheel"

[Code]
// Stop the old "Wheel" (from before the rename) so its folder can be removed.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Wheel.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
