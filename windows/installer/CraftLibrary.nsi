; Installer for Craft Library (the C# version).
; Installs just for the current Windows user, so no administrator password is needed.
; Built by windows/build.sh (Linux) or windows/build.cmd (Windows); both pass in the version number.

Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !error "Run the build script, which passes /DVERSION=x.y.z"
!endif

!define APPNAME "Craft Library"
!define EXENAME "CraftLibrary.exe"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CraftLibraryCSharp"

Name "${APPNAME}"
OutFile "..\dist\Craft-Library-Setup-${VERSION}.exe"
; A different folder from the old (Electron) version, so the two never mix.
InstallDir "$LOCALAPPDATA\Programs\CraftLibrary"
InstallDirRegKey HKCU "Software\CraftLibrary" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "Craft Library ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Craft Library"
VIAddVersionKey "LegalCopyright" "Craft Library"

!define MUI_ICON "..\CraftLibrary.App\app.ico"
!define MUI_UNICON "..\CraftLibrary.App\app.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Welcome to Craft Library"
!define MUI_WELCOMEPAGE_TEXT "This will install Craft Library ${VERSION} on this computer.$\r$\n$\r$\nCraft Library only works inside the folders you choose. It never deletes files outright: removed files go to the Recycle Bin (or, on portable drives, to a Removed files folder on that drive), and Undo puts things back.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Open Craft Library now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  ; If Craft Library is open, ask to close it first (the program file can't be replaced while running).
  retry:
  ClearErrors
  FileOpen $0 "$INSTDIR\${EXENAME}" a
  IfErrors 0 +3
    IfFileExists "$INSTDIR\${EXENAME}" 0 +2
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Please close Craft Library, then click Retry." IDRETRY retry IDCANCEL cancelled
  FileClose $0

  SetOutPath "$INSTDIR"
  File "..\publish\${EXENAME}"
  File /oname=icon.ico "..\CraftLibrary.App\app.ico"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\icon.ico" 0
  CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\icon.ico" 0

  WriteRegStr HKCU "Software\CraftLibrary" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${REGKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REGKEY}" "Publisher" "Craft Library"
  WriteRegStr HKCU "${REGKEY}" "DisplayIcon" "$INSTDIR\icon.ico"
  WriteRegStr HKCU "${REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${REGKEY}" "NoRepair" 1
  Return

  cancelled:
  Abort "Craft Library was left as it was."
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\icon.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${REGKEY}"
  DeleteRegKey HKCU "Software\CraftLibrary"
  ; Her settings, notes and saved thumbnails (in %LOCALAPPDATA%\Craft Library) are kept,
  ; so reinstalling picks up where she left off. Her craft files are never touched.
SectionEnd
