; Xloer portable: one .exe that runs without installing.
;
; electron-builder's own portable unpacks the whole app (about 250 MB) to a
; temp folder on every start and deletes it again on exit, so every start
; waits for that and for the virus scan of every unpacked file. This one
; unpacks a build once, to %LOCALAPPDATA%\Xloer\portable\<version>-<build>,
; and from then on only starts it from there. Folders of other builds are
; removed once nothing runs from them.
;
; scripts/release.js builds it with electron-builder's makensis and passes
; VERSION, BUILD_ID, APP_DIR (win-unpacked), ICON, OUT_FILE and COPYRIGHT.

Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SilentInstall silent
; Reading all 100 MB for a checksum on every start would undo the point.
CRCCheck off
SetCompressor /SOLID lzma
SetDatablockOptimize on

!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define ROOT "$LOCALAPPDATA\Xloer\portable"
!define BUILD "${VERSION}-${BUILD_ID}"
!define DIR "${ROOT}\${BUILD}"
; Written last, so a folder without it was not unpacked completely.
!define MARKER "${DIR}\.unpacked"

Name "Xloer"
OutFile "${OUT_FILE}"
Icon "${ICON}"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "Xloer"
VIAddVersionKey "FileDescription" "Xloer"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "${COPYRIGHT}"

Var mutex

Function Unpack
  Banner::show /set 76 "Xloer hazırlanıyor…" "Bu sürümün ilk açılışı; sonraki açılışlar hızlı olacak."
  RMDir /r "${DIR}"
  SetOutPath "${DIR}"
  ClearErrors
  File /r "${APP_DIR}\*.*"
  Banner::destroy
  ${If} ${Errors}
  ${OrIfNot} ${FileExists} "${DIR}\Xloer.exe"
  ${OrIfNot} ${FileExists} "${DIR}\resources\app.asar"
    SetOutPath "$TEMP"
    RMDir /r "${DIR}"
    MessageBox MB_ICONSTOP "Xloer açılamadı: dosyaları$\r$\n${DIR}$\r$\nklasörüne çıkarılamadı. Diskte yer olduğundan emin olup tekrar dene."
    Call Unlock
    SetErrorLevel 2
    Quit
  ${EndIf}
  FileOpen $0 "${MARKER}" w
  FileWrite $0 "${BUILD}"
  FileClose $0
FunctionEnd

; A folder that something still runs from cannot be renamed; it stays until
; a later start finds it unused.
Function RemoveOtherBuilds
  FindFirst $R0 $R1 "${ROOT}\*"
  ${DoWhile} $R1 != ""
    ${If} $R1 != "."
    ${AndIf} $R1 != ".."
    ${AndIf} $R1 != "${BUILD}"
    ${AndIf} ${FileExists} "${ROOT}\$R1\*.*"
      ClearErrors
      Rename "${ROOT}\$R1" "${ROOT}\$R1.del"
      ${IfNot} ${Errors}
        RMDir /r "${ROOT}\$R1.del"
      ${EndIf}
    ${EndIf}
    FindNext $R0 $R1
  ${Loop}
  FindClose $R0
FunctionEnd

; Two starts at the same moment: the second waits until the first has unpacked.
Function Lock
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\XloerPortableUnpack") p .r0'
  StrCpy $mutex $0
  System::Call 'kernel32::WaitForSingleObject(p r0, i 300000) i'
FunctionEnd

Function Unlock
  System::Call 'kernel32::ReleaseMutex(p $mutex) i'
  System::Call 'kernel32::CloseHandle(p $mutex) i'
FunctionEnd

Section
  Call Lock
  ${IfNot} ${FileExists} "${MARKER}"
    Call Unpack
    Call RemoveOtherBuilds
  ${EndIf}
  Call Unlock

  ; The app registers this .exe (not the unpacked one) to start with Windows
  ; and sends a portable copy to the download page for updates.
  System::Call 'kernel32::SetEnvironmentVariableW(w "PORTABLE_EXECUTABLE_FILE", w "$EXEPATH") i'
  System::Call 'kernel32::SetEnvironmentVariableW(w "PORTABLE_EXECUTABLE_DIR", w "$EXEDIR") i'
  ${GetParameters} $1
  SetOutPath "${DIR}"
  ClearErrors
  Exec '"${DIR}\Xloer.exe" $1'
  ${If} ${Errors}
    ; Unpack again next time.
    Delete "${MARKER}"
    MessageBox MB_ICONSTOP "Xloer başlatılamadı. Tekrar dene; olmazsa kurulum sürümünü (Xloer-Setup) kullan."
    SetErrorLevel 1
  ${EndIf}
SectionEnd
