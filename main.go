package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/fsnotify/fsnotify"
	"github.com/getlantern/systray"
	"github.com/skratchdot/open-golang/open"
)

type Config struct {
	WatchPath string `json:"watchPath"`
	ApiUrl    string `json:"apiUrl"`
	ApiKey    string `json:"apiKey"`
	StoreId   string `json:"storeId"`
}

var (
	config       Config
	watcher      *fsnotify.Watcher
	isWatching   bool
	configPath   string
	uploadQueue  = make(chan string, 1000)
)

func main() {
	// Setup config path
	homeDir, _ := os.UserHomeDir()
	configDir := filepath.Join(homeDir, ".printago-folder-watch")
	os.MkdirAll(configDir, 0755)
	configPath = filepath.Join(configDir, "config.json")

	// Load config
	loadConfig()

	// Start system tray
	systray.Run(onReady, onExit)
}

func onReady() {
	systray.SetTitle("Printago Folder Watch")
	systray.SetTooltip("Printago Folder Watch")

	// Menu items
	mStart := systray.AddMenuItem("Start Watching", "Start monitoring folder")
	mStop := systray.AddMenuItem("Stop Watching", "Stop monitoring")
	systray.AddSeparator()
	mConfigure := systray.AddMenuItem("Configure...", "Configure settings")
	systray.AddSeparator()
	mExit := systray.AddMenuItem("Exit", "Exit application")

	mStop.Disable()

	// Start upload processor
	go processUploads()

	// Auto-start if configured
	if config.WatchPath != "" && config.ApiUrl != "" && config.ApiKey != "" && config.StoreId != "" {
		go startWatching()
		mStart.Disable()
		mStop.Enable()
	}

	// Handle menu clicks
	go func() {
		for {
			select {
			case <-mStart.ClickedCh:
				if !isWatching {
					go startWatching()
					mStart.Disable()
					mStop.Enable()
					showNotification("Started", "Folder watching started")
				}

			case <-mStop.ClickedCh:
				if isWatching {
					stopWatching()
					mStart.Enable()
					mStop.Disable()
					showNotification("Stopped", "Folder watching stopped")
				}

			case <-mConfigure.ClickedCh:
				// Open config file for editing
				ensureConfigExists()
				open.Run(configPath)
				showNotification("Configure", "Config file opened. Restart after saving.")

			case <-mExit.ClickedCh:
				stopWatching()
				systray.Quit()
				return
			}
		}
	}()
}

func onExit() {
	stopWatching()
}

func loadConfig() {
	data, err := ioutil.ReadFile(configPath)
	if err != nil {
		config = Config{}
		return
	}
	json.Unmarshal(data, &config)
}

func saveConfig() {
	data, _ := json.MarshalIndent(config, "", "  ")
	ioutil.WriteFile(configPath, data, 0644)
}

func ensureConfigExists() {
	if _, err := os.Stat(configPath); os.IsNotExist(err) {
		config = Config{
			WatchPath: "D:\\Onedrive Humpf Tech\\OneDrive - Humpf Tech LLC\\Documents\\3DPrinting\\",
			ApiUrl:    "https://new-api.printago.io/",
			ApiKey:    "dpv8gz71fuob00qxbmnw9nm0uga53r88aw1bbrzjhvtzebjtcz41cf3d6s7hfy48t03egwkg",
			StoreId:   "sb3bexu83dpm0gry8u265amx",
		}
		saveConfig()
	}
}

func startWatching() {
	if isWatching {
		return
	}

	var err error
	watcher, err = fsnotify.NewWatcher()
	if err != nil {
		showNotification("Error", "Failed to create watcher")
		return
	}

	err = watcher.Add(config.WatchPath)
	if err != nil {
		showNotification("Error", "Failed to watch path: "+config.WatchPath)
		return
	}

	isWatching = true

	// Upload existing files
	go uploadExistingFiles()

	// Watch for changes
	go func() {
		for {
			select {
			case event, ok := <-watcher.Events:
				if !ok {
					return
				}
				if event.Op&fsnotify.Write == fsnotify.Write || event.Op&fsnotify.Create == fsnotify.Create {
					uploadQueue <- event.Name
				}
			case err, ok := <-watcher.Errors:
				if !ok {
					return
				}
				fmt.Println("Watcher error:", err)
			}
		}
	}()
}

func stopWatching() {
	if watcher != nil {
		watcher.Close()
		isWatching = false
	}
}

func uploadExistingFiles() {
	filepath.Walk(config.WatchPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		uploadQueue <- path
		return nil
	})
}

func processUploads() {
	for filePath := range uploadQueue {
		time.Sleep(1 * time.Second) // Debounce
		uploadFile(filePath)
	}
}

func uploadFile(filePath string) {
	// Read file
	fileData, err := ioutil.ReadFile(filePath)
	if err != nil {
		fmt.Println("Failed to read file:", err)
		return
	}

	// Get relative path
	relPath, _ := filepath.Rel(config.WatchPath, filePath)
	cloudPath := strings.ReplaceAll(relPath, "\\", "/")

	// Step 1: Get signed URL
	apiUrl := strings.TrimSuffix(config.ApiUrl, "/")
	requestBody, _ := json.Marshal(map[string]interface{}{
		"filenames": []string{cloudPath},
	})

	req, _ := http.NewRequest("POST", apiUrl+"/v1/storage/signed-upload-urls", bytes.NewBuffer(requestBody))
	req.Header.Set("authorization", "ApiKey "+config.ApiKey)
	req.Header.Set("x-printago-storeid", config.StoreId)
	req.Header.Set("content-type", "application/json")

	client := &http.Client{Timeout: 30 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		fmt.Println("Failed to get signed URL:", err)
		return
	}
	defer resp.Body.Close()

	var signedUrlResponse struct {
		SignedUrls []struct {
			UploadUrl string `json:"uploadUrl"`
		} `json:"signedUrls"`
	}

	body, _ := io.ReadAll(resp.Body)
	json.Unmarshal(body, &signedUrlResponse)

	if len(signedUrlResponse.SignedUrls) == 0 {
		fmt.Println("No signed URL returned")
		return
	}

	uploadUrl := signedUrlResponse.SignedUrls[0].UploadUrl

	// Step 2: Upload file
	uploadReq, _ := http.NewRequest("PUT", uploadUrl, bytes.NewReader(fileData))
	uploadReq.ContentLength = int64(len(fileData))

	uploadResp, err := client.Do(uploadReq)
	if err != nil {
		fmt.Println("Upload failed:", err)
		return
	}
	defer uploadResp.Body.Close()

	if uploadResp.StatusCode >= 200 && uploadResp.StatusCode < 300 {
		fmt.Println("✓ Uploaded:", cloudPath)
	} else {
		fmt.Println("✗ Upload failed:", cloudPath, "Status:", uploadResp.StatusCode)
	}
}

func showNotification(title, message string) {
	systray.SetTooltip(title + ": " + message)
	fmt.Println(title+":", message)
}
