; Bundles the VMulti VirtualHID driver (X9VoiD/vmulti-bin v1.0) and installs
; it during setup so Windows Ink pressure works out of the box — no separate
; download for the user. Requires perMachine install (devcon needs admin).
;
; The driver files are staged by scripts/build.sh into $%VMULTI_DIR% (compile-
; time environment variable) before makensis runs.

!macro NSIS_HOOK_POSTINSTALL
  DetailPrint "Installing VMulti VirtualHID driver (Windows Ink pressure)..."
  SetOutPath "$INSTDIR\vmulti"
  File /r "$%VMULTI_DIR%\*"

  ; Remove-then-install: `devcon install` always creates a new root device
  ; node, so a reinstall/upgrade would otherwise stack duplicates. Remove
  ; fails harmlessly when no device exists yet.
  nsExec::ExecToLog '"$INSTDIR\vmulti\devcon.exe" remove "pentablet\hid"'
  Pop $0
  nsExec::ExecToLog '"$INSTDIR\vmulti\devcon.exe" install "$INSTDIR\vmulti\vmulti.inf" "pentablet\hid"'
  Pop $0
  ${If} $0 != 0
    DetailPrint "VMulti install returned $0 — Windows Ink output may need a reboot or manual driver install."
  ${EndIf}
!macroend

!macro NSIS_HOOK_POSTUNINSTALL
  ; Leave the driver installed: other tablet software (osu! setups etc.) may
  ; share the same VMulti device. It is inert without a writer.
!macroend
