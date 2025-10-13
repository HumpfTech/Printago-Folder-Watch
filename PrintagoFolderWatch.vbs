Set objShell = CreateObject("WScript.Shell")
Set objFSO = CreateObject("Scripting.FileSystemObject")

' Get the directory where this script is located
strScriptPath = objFSO.GetParentFolderName(WScript.ScriptFullName)

' Check if Node.js is installed
On Error Resume Next
objShell.Run "node --version", 0, True
If Err.Number <> 0 Then
    MsgBox "Node.js is not installed. Please install Node.js from nodejs.org", vbCritical, "Error"
    WScript.Quit
End If
On Error Goto 0

' Check if node_modules exists
If Not objFSO.FolderExists(strScriptPath & "\node_modules") Then
    MsgBox "Installing dependencies... This may take a minute.", vbInformation, "First Run"
    objShell.CurrentDirectory = strScriptPath
    objShell.Run "cmd /c npm install", 1, True
End If

' Run the tray script
strPSScript = strScriptPath & "\tray.ps1"
strCommand = "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & strPSScript & """"
objShell.Run strCommand, 0, False

Set objShell = Nothing
Set objFSO = Nothing
