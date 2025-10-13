# Printago Folder Watch - System Tray Application
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Load config
$configDir = Join-Path $env:USERPROFILE ".printago-folder-watch"
$configFile = Join-Path $configDir "config.json"

if (!(Test-Path $configDir)) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
}

function Load-Config {
    if (Test-Path $configFile) {
        return Get-Content $configFile | ConvertFrom-Json
    }
    return @{
        watchPath = ""
        apiUrl = ""
        apiKey = ""
        storeId = ""
    }
}

function Save-Config($config) {
    $config | ConvertTo-Json | Set-Content $configFile
}

$config = Load-Config
$script:nodeProcess = $null
$script:isWatching = $false

# Create icon
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon("C:\Windows\System32\imageres.dll")
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = $icon
$tray.Visible = $true
$tray.Text = "Printago Folder Watch - Stopped"

# Create context menu
$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip

$startItem = $contextMenu.Items.Add("Start Watching")
$stopItem = $contextMenu.Items.Add("Stop Watching")
$contextMenu.Items.Add("-") | Out-Null
$configItem = $contextMenu.Items.Add("Configure...")
$contextMenu.Items.Add("-") | Out-Null
$exitItem = $contextMenu.Items.Add("Exit")

$stopItem.Enabled = $false

# Start watching
$startItem.Add_Click({
    if (!$script:isWatching) {
        $nodeExe = Join-Path $PSScriptRoot "node_modules\.bin\node.cmd"
        $appJs = Join-Path $PSScriptRoot "app-auto.js"

        $script:nodeProcess = Start-Process -FilePath "node" -ArgumentList $appJs -NoNewWindow -PassThru
        $script:isWatching = $true
        $tray.Text = "Printago Folder Watch - Running"
        $tray.ShowBalloonTip(3000, "Printago", "Started watching folder", [System.Windows.Forms.ToolTipIcon]::Info)
        $startItem.Enabled = $false
        $stopItem.Enabled = $true
    }
})

# Stop watching
$stopItem.Add_Click({
    if ($script:isWatching -and $script:nodeProcess) {
        Stop-Process -Id $script:nodeProcess.Id -Force
        $script:isWatching = $false
        $tray.Text = "Printago Folder Watch - Stopped"
        $tray.ShowBalloonTip(3000, "Printago", "Stopped watching", [System.Windows.Forms.ToolTipIcon]::Info)
        $startItem.Enabled = $true
        $stopItem.Enabled = $false
    }
})

# Configure
$configItem.Add_Click({
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Printago Configuration"
    $form.Size = New-Object System.Drawing.Size(500, 300)
    $form.StartPosition = "CenterScreen"

    $y = 20

    # Watch Path
    $label1 = New-Object System.Windows.Forms.Label
    $label1.Location = New-Object System.Drawing.Point(10, $y)
    $label1.Size = New-Object System.Drawing.Size(120, 20)
    $label1.Text = "Watch Path:"
    $form.Controls.Add($label1)

    $textBox1 = New-Object System.Windows.Forms.TextBox
    $textBox1.Location = New-Object System.Drawing.Point(130, $y)
    $textBox1.Size = New-Object System.Drawing.Size(340, 20)
    $textBox1.Text = $config.watchPath
    $form.Controls.Add($textBox1)

    $y += 35

    # API URL
    $label2 = New-Object System.Windows.Forms.Label
    $label2.Location = New-Object System.Drawing.Point(10, $y)
    $label2.Size = New-Object System.Drawing.Size(120, 20)
    $label2.Text = "API URL:"
    $form.Controls.Add($label2)

    $textBox2 = New-Object System.Windows.Forms.TextBox
    $textBox2.Location = New-Object System.Drawing.Point(130, $y)
    $textBox2.Size = New-Object System.Drawing.Size(340, 20)
    $textBox2.Text = $config.apiUrl
    $form.Controls.Add($textBox2)

    $y += 35

    # API Key
    $label3 = New-Object System.Windows.Forms.Label
    $label3.Location = New-Object System.Drawing.Point(10, $y)
    $label3.Size = New-Object System.Drawing.Size(120, 20)
    $label3.Text = "API Key:"
    $form.Controls.Add($label3)

    $textBox3 = New-Object System.Windows.Forms.TextBox
    $textBox3.Location = New-Object System.Drawing.Point(130, $y)
    $textBox3.Size = New-Object System.Drawing.Size(340, 20)
    $textBox3.Text = $config.apiKey
    $textBox3.UseSystemPasswordChar = $true
    $form.Controls.Add($textBox3)

    $y += 35

    # Store ID
    $label4 = New-Object System.Windows.Forms.Label
    $label4.Location = New-Object System.Drawing.Point(10, $y)
    $label4.Size = New-Object System.Drawing.Size(120, 20)
    $label4.Text = "Store ID:"
    $form.Controls.Add($label4)

    $textBox4 = New-Object System.Windows.Forms.TextBox
    $textBox4.Location = New-Object System.Drawing.Point(130, $y)
    $textBox4.Size = New-Object System.Drawing.Size(340, 20)
    $textBox4.Text = $config.storeId
    $form.Controls.Add($textBox4)

    $y += 50

    # Save button
    $saveButton = New-Object System.Windows.Forms.Button
    $saveButton.Location = New-Object System.Drawing.Point(170, $y)
    $saveButton.Size = New-Object System.Drawing.Size(150, 30)
    $saveButton.Text = "Save"
    $saveButton.Add_Click({
        $script:config = @{
            watchPath = $textBox1.Text
            apiUrl = $textBox2.Text
            apiKey = $textBox3.Text
            storeId = $textBox4.Text
        }
        Save-Config $script:config
        [System.Windows.Forms.MessageBox]::Show("Configuration saved!", "Success")
        $form.Close()
    })
    $form.Controls.Add($saveButton)

    $form.ShowDialog() | Out-Null
})

# Exit
$exitItem.Add_Click({
    if ($script:isWatching -and $script:nodeProcess) {
        Stop-Process -Id $script:nodeProcess.Id -Force
    }
    $tray.Visible = $false
    $tray.Dispose()
    [System.Windows.Forms.Application]::Exit()
})

$tray.ContextMenuStrip = $contextMenu

# Start application context
$appContext = New-Object System.Windows.Forms.ApplicationContext
[System.Windows.Forms.Application]::Run($appContext)
