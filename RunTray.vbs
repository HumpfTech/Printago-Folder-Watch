Set objShell = CreateObject("WScript.Shell")
Set objFSO = CreateObject("Scripting.FileSystemObject")

' Get the directory where this script is located
strScriptPath = objFSO.GetParentFolderName(WScript.ScriptFullName)
strPSScript = strScriptPath & "\tray.ps1"

' Run PowerShell hidden
strCommand = "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & strPSScript & """"
objShell.Run strCommand, 0, False

Set objShell = Nothing
Set objFSO = Nothing
