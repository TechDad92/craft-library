; Installer for Craft Library
; Installs just for the current Windows user (no administrator password needed).

Unicode true
!include "MUI2.nsh"

!define APPNAME "Craft Library"
!define EXENAME "Craft Library.exe"
!define VERSION "0.2.3"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\CraftLibrary"

Name "${APPNAME}"
OutFile "../dist/Craft-Library-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Craft Library"
InstallDirRegKey HKCU "Software\CraftLibrary" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "Craft Library ${VERSION}"

VIProductVersion "0.2.3.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "Craft Library"
VIAddVersionKey "LegalCopyright" "Craft Library"

!define MUI_ICON "icon.ico"
!define MUI_UNICON "icon.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Welcome to Craft Library"
!define MUI_WELCOMEPAGE_TEXT "This will install Craft Library on this computer.$\r$\n$\r$\nCraft Library only works inside the folders you choose. It never deletes files outright: removed files go to the Recycle Bin (or, on portable drives, to a Removed files folder on that drive), and Undo puts things back.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Open Craft Library now"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "../dist/win-unpacked/*.*"
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
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${REGKEY}"
  DeleteRegKey HKCU "Software\CraftLibrary"
SectionEnd
