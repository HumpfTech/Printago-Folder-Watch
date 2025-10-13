#!/usr/bin/env python3
"""
Printago Folder Watch - Secure directory monitoring and file sync for Printago
"""

import tkinter as tk
from tkinter import ttk, filedialog, scrolledtext, messagebox
import threading
import time
import os
import json
import hashlib
from pathlib import Path
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler
import requests
from queue import Queue

CONFIG_FILE = "config.json"

# Load environment variables if .env file exists
def load_env():
    """Load environment variables from .env file"""
    env_vars = {}
    if os.path.exists('.env'):
        with open('.env', 'r') as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith('#') and '=' in line:
                    key, value = line.split('=', 1)
                    env_vars[key.strip()] = value.strip()
    return env_vars

ENV_VARS = load_env()

class PrintagoClient:
    def __init__(self, api_url, api_key, store_id=None):
        self.api_url = api_url.rstrip('/')
        self.api_key = api_key
        self.store_id = store_id
        self.session = requests.Session()

        # Correct Printago API authentication format
        headers = {
            'authorization': f'ApiKey {api_key}',
            'User-Agent': 'PrintagoFolderWatch/1.0'
        }

        # Add store ID if provided
        if store_id:
            headers['x-printago-storeid'] = store_id

        self.session.headers.update(headers)

        # Cache for folders (folderId mapped to folder path)
        self.folder_cache = {}

    def test_connection(self):
        """Test API connection"""
        try:
            # Test with web-init endpoint which should be available
            response = self.session.get(f'{self.api_url}/v1/web-init', timeout=5)
            return response.status_code == 200
        except Exception as e:
            print(f"Connection test failed: {e}")
            return False

    def list_folders(self):
        """Get all folders from Printago and build hierarchical cache"""
        try:
            response = self.session.get(f'{self.api_url}/v1/folders', timeout=30)
            response.raise_for_status()
            folders = response.json()

            # Build maps: id -> folder, id -> children
            id_to_folder = {f['id']: f for f in folders}
            id_to_children = {}

            # Build parent-child relationships
            for folder in folders:
                parent_id = folder.get('parentId')
                if parent_id:
                    if parent_id not in id_to_children:
                        id_to_children[parent_id] = []
                    id_to_children[parent_id].append(folder['id'])

            # Build full paths recursively
            def build_path(folder_id, path_parts=[]):
                folder = id_to_folder.get(folder_id)
                if not folder:
                    return None

                current_parts = path_parts + [folder['name']]
                full_path = '/'.join(current_parts)

                # Add to cache
                self.folder_cache[full_path] = folder_id

                # Process children
                for child_id in id_to_children.get(folder_id, []):
                    build_path(child_id, current_parts)

                return full_path

            # Start with root folders (no parent)
            for folder in folders:
                if folder.get('parentId') is None:
                    build_path(folder['id'])

            return folders
        except Exception as e:
            raise Exception(f"Failed to list folders: {str(e)}")

    def create_folder(self, folder_path):
        """Create a folder in Printago with nested hierarchy support"""
        try:
            # Check if already exists in cache
            if folder_path in self.folder_cache:
                return self.folder_cache[folder_path]

            # Split path into parts for nested folders
            # e.g., "alphabet-letter-keychain-model_files/subfolder" -> ["alphabet-letter-keychain-model_files", "subfolder"]
            parts = folder_path.split('/')
            parent_id = None
            current_path = ''

            # Create each level of the hierarchy
            for i, part in enumerate(parts):
                # Build current path
                if current_path:
                    current_path = f"{current_path}/{part}"
                else:
                    current_path = part

                # Check if this level already exists
                if current_path in self.folder_cache:
                    parent_id = self.folder_cache[current_path]
                    continue

                # Create folder at this level
                response = self.session.post(
                    f'{self.api_url}/v1/folders',
                    json={'name': part, 'type': 'part', 'parentId': parent_id},
                    timeout=30
                )
                response.raise_for_status()
                folder_data = response.json()
                folder_id = folder_data.get('id')

                # Add to cache with full path as key
                self.folder_cache[current_path] = folder_id
                parent_id = folder_id

            return parent_id
        except Exception as e:
            raise Exception(f"Failed to create folder '{folder_path}': {str(e)}")

    def delete_folder(self, folder_id):
        """Delete a folder from Printago"""
        try:
            response = self.session.delete(f'{self.api_url}/v1/folders/{folder_id}', timeout=30)
            response.raise_for_status()

            # Remove from cache
            self.folder_cache = {k: v for k, v in self.folder_cache.items() if v != folder_id}
            return True
        except Exception as e:
            raise Exception(f"Failed to delete folder: {str(e)}")

    def list_all_parts(self):
        """Get all parts from Printago"""
        try:
            response = self.session.get(f'{self.api_url}/v1/parts', timeout=30)
            response.raise_for_status()
            return response.json()
        except Exception as e:
            raise Exception(f"Failed to list parts: {str(e)}")

    def upload_file(self, relative_path, file_path):
        """Upload 3D model file to Printago - WORKING METHOD!"""
        try:
            import hashlib

            # Sanitize path and preserve folder structure
            safe_path = self._sanitize_path(relative_path)
            filename = os.path.basename(safe_path)

            # Extract folder path and create folder in Printago if needed
            # e.g., "KeyChains/letter-A.3mf" -> folder: "KeyChains", name: "letter-A"
            folder_path = os.path.dirname(safe_path).replace('\\', '/')
            name = os.path.splitext(filename)[0]

            # Create folder if file is in a subdirectory
            folder_id = None
            if folder_path:
                folder_id = self.create_folder(folder_path)

            # Detect file type
            ext = os.path.splitext(filename)[1].lower().lstrip('.')
            file_type_map = {'3mf': '3mf', 'gcode': 'gcode3mf', 'scad': 'scad', 'step': 'step', 'stp': 'step', 'stl': 'stl'}
            file_type = file_type_map.get(ext, '3mf')

            # Step 1: Get signed upload URL from Printago
            signed_response = self.session.post(
                f'{self.api_url}/v1/storage/signed-upload-urls',
                json={'filenames': [filename]},
                timeout=30
            )
            signed_response.raise_for_status()
            signed_data = signed_response.json()
            upload_url = signed_data['signedUrls'][0]['uploadUrl']
            cloud_path = signed_data['signedUrls'][0]['path']

            # Step 2: Read file and calculate hash
            with open(file_path, 'rb') as f:
                file_content = f.read()
                file_hash = hashlib.sha256(file_content).hexdigest()

            # Step 3: Upload to Google Cloud Storage
            # Use requests.put directly (not session) to avoid auth headers breaking GCS signature
            import requests
            upload_response = requests.put(upload_url, data=file_content, timeout=120)
            upload_response.raise_for_status()

            # Step 4: Create part with uploaded file
            part_payload = {
                'name': name,
                'type': file_type,
                'description': 'Auto-uploaded from folder watch',
                'fileUris': [cloud_path],
                'fileHashes': [file_hash],
                'parameters': [],
                'printTags': {},
                'overriddenProcessProfileId': None,
                'folderId': folder_id  # Assign to folder
            }

            part_response = self.session.post(f'{self.api_url}/v1/parts', json=part_payload, timeout=60)
            part_response.raise_for_status()
            return True

        except Exception as e:
            raise Exception(f"Upload failed: {str(e)}")

    def delete_file(self, relative_path):
        """Delete file from Printago by finding and deleting the part"""
        try:
            # Convert file path to part name and folder (same logic as upload)
            safe_path = self._sanitize_path(relative_path)
            filename = os.path.basename(safe_path)
            folder_path = os.path.dirname(safe_path).replace('\\', '/')
            part_name = os.path.splitext(filename)[0]

            # Get folder ID if in subdirectory
            folder_id = None
            if folder_path and folder_path in self.folder_cache:
                folder_id = self.folder_cache[folder_path]

            # Search for parts with this name
            search_response = self.session.get(
                f'{self.api_url}/v1/parts',
                params={'name': part_name},
                timeout=30
            )
            search_response.raise_for_status()
            parts = search_response.json()

            # Delete matching parts (match name AND folderId)
            deleted_count = 0
            if isinstance(parts, list):
                for part in parts:
                    if part.get('name') == part_name and part.get('folderId') == folder_id:
                        part_id = part.get('id')
                        delete_response = self.session.delete(
                            f'{self.api_url}/v1/parts/{part_id}',
                            timeout=30
                        )
                        delete_response.raise_for_status()
                        deleted_count += 1

            return deleted_count > 0
        except Exception as e:
            raise Exception(f"Delete failed: {str(e)}")

    def _sanitize_path(self, path):
        """Sanitize file path to prevent path traversal"""
        return path.replace('..', '').lstrip('/\\').replace('<', '_').replace('>', '_').replace(':', '_').replace('"', '_').replace('|', '_').replace('?', '_').replace('*', '_')


class FileWatchHandler(FileSystemEventHandler):
    def __init__(self, watch_path, printago_client, log_callback):
        self.watch_path = Path(watch_path)
        self.client = printago_client
        self.log = log_callback
        self.upload_queue = Queue()
        self.max_file_size = 500 * 1024 * 1024  # 500MB - increased for 3D model files
        self.initial_sync_done = False

        # Start upload worker thread
        self.worker_thread = threading.Thread(target=self._upload_worker, daemon=True)
        self.worker_thread.start()

    def initial_sync(self):
        """TRUE MIRROR SYNC - Compare local files with Printago and sync differences"""
        self.log("🔍 Starting mirror sync...")

        try:
            # Step 1: Load folder cache from Printago
            self.log("📂 Loading folders from Printago...")
            self.client.list_folders()

            # Step 2: Get all parts from Printago
            self.log("📥 Fetching all parts from Printago...")
            printago_parts = self.client.list_all_parts()

            # Build map of Printago parts: (folder_path, filename) -> part_id
            printago_map = {}
            for part in printago_parts:
                # Get folder path from folderId
                folder_path = ''
                part_folder_id = part.get('folderId')
                if part_folder_id:
                    # Find folder name from cache
                    for fpath, fid in self.client.folder_cache.items():
                        if fid == part_folder_id:
                            folder_path = fpath
                            break

                part_name = part.get('name', '')
                part_id = part.get('id')
                printago_map[(folder_path, part_name)] = part_id

            self.log(f"📊 Found {len(printago_parts)} parts in Printago")

            # Step 3: Scan local files
            self.log("📁 Scanning local files...")
            local_files = {}  # (folder_path, filename) -> file_path
            all_local_folders = set()

            for root, dirs, files in os.walk(self.watch_path):
                # Skip hidden directories
                dirs[:] = [d for d in dirs if not d.startswith('.')]

                # Track all local folder paths
                rel_dir = str(Path(root).relative_to(self.watch_path)).replace('\\', '/')
                if rel_dir != '.':
                    all_local_folders.add(rel_dir)

                for file in files:
                    # Skip hidden files
                    if file.startswith('.'):
                        continue

                    file_path = os.path.join(root, file)
                    rel_path = str(Path(file_path).relative_to(self.watch_path))
                    folder_path = os.path.dirname(rel_path).replace('\\', '/')
                    filename_no_ext = os.path.splitext(os.path.basename(file))[0]

                    local_files[(folder_path, filename_no_ext)] = file_path

                    # Add parent folder to set
                    if folder_path:
                        all_local_folders.add(folder_path)

            self.log(f"📊 Found {len(local_files)} local files in {len(all_local_folders)} folders")

            # Step 4: Find files to upload (in local but not in Printago)
            to_upload = []
            for key, file_path in local_files.items():
                if key not in printago_map:
                    to_upload.append(file_path)

            # Step 5: Find parts to delete (in Printago but not local)
            to_delete = []
            for key, part_id in printago_map.items():
                if key not in local_files:
                    to_delete.append(part_id)

            # Step 6: Find folders to delete (in Printago but not local)
            printago_folders = set(self.client.folder_cache.keys())
            folders_to_delete = printago_folders - all_local_folders

            # Log summary
            self.log(f"📤 {len(to_upload)} files to upload")
            self.log(f"🗑️ {len(to_delete)} parts to delete from Printago")
            self.log(f"📁 {len(folders_to_delete)} folders to delete from Printago")

            # Step 7: Queue uploads
            for file_path in to_upload:
                self.upload_queue.put(('upload', file_path))

            # Step 8: Delete orphaned parts
            for part_id in to_delete:
                self.upload_queue.put(('delete_part_id', part_id))

            # Step 9: Delete orphaned folders (after parts are deleted)
            for folder_path in folders_to_delete:
                folder_id = self.client.folder_cache.get(folder_path)
                if folder_id:
                    self.upload_queue.put(('delete_folder_id', folder_id))

            self.log("✅ Mirror sync initiated")
            self.initial_sync_done = True

        except Exception as e:
            self.log(f"❌ Mirror sync error: {str(e)}")

    def on_created(self, event):
        if not event.is_directory and self._is_safe_path(event.src_path):
            self.log(f"📁 File added: {event.src_path}")
            self.upload_queue.put(('upload', event.src_path))

    def on_modified(self, event):
        if not event.is_directory and self._is_safe_path(event.src_path):
            self.log(f"📝 File modified: {event.src_path}")
            self.upload_queue.put(('upload', event.src_path))

    def on_deleted(self, event):
        if not event.is_directory and self._is_safe_path(event.src_path):
            self.log(f"🗑️ File deleted locally: {event.src_path}")
            self.upload_queue.put(('delete', event.src_path))

    def _upload_worker(self):
        """Worker thread to process upload and delete queue"""
        while True:
            try:
                action, data = self.upload_queue.get(timeout=1)

                if action == 'upload':
                    # Wait for file to be stable
                    time.sleep(2)

                    if os.path.exists(data):
                        self._upload_file(data)

                elif action == 'delete':
                    # Delete by file path
                    self._delete_file(data)

                elif action == 'delete_part_id':
                    # Delete by part ID (for mirror sync)
                    self._delete_part_by_id(data)

                elif action == 'delete_folder_id':
                    # Delete folder by ID (for mirror sync)
                    self._delete_folder_by_id(data)

                self.upload_queue.task_done()
            except:
                continue

    def _upload_file(self, file_path):
        """Upload a single file"""
        try:
            # Security checks
            if not self._is_safe_path(file_path):
                self.log(f"⚠️ Blocked suspicious path: {file_path}")
                return

            file_size = os.path.getsize(file_path)
            if file_size > self.max_file_size:
                self.log(f"⚠️ File too large (max 500MB): {file_path}")
                return

            rel_path = self._get_relative_path(file_path)
            self.client.upload_file(rel_path, file_path)
            self.log(f"✅ Uploaded: {file_path}")

        except Exception as e:
            self.log(f"❌ Upload failed: {file_path} - {str(e)}")

    def _delete_file(self, file_path):
        """Delete a file from Printago"""
        try:
            rel_path = self._get_relative_path(file_path)
            if self.client.delete_file(rel_path):
                self.log(f"🗑️ Deleted from Printago: {file_path}")
            else:
                self.log(f"⚠️ File not found in Printago: {file_path}")

        except Exception as e:
            self.log(f"❌ Delete failed: {file_path} - {str(e)}")

    def _delete_part_by_id(self, part_id):
        """Delete a part from Printago by ID (for mirror sync)"""
        try:
            response = self.client.session.delete(
                f'{self.client.api_url}/v1/parts/{part_id}',
                timeout=30
            )
            response.raise_for_status()
            self.log(f"🗑️ Deleted orphaned part: {part_id}")
        except Exception as e:
            self.log(f"❌ Failed to delete part {part_id}: {str(e)}")

    def _delete_folder_by_id(self, folder_id):
        """Delete a folder from Printago by ID (for mirror sync)"""
        try:
            self.client.delete_folder(folder_id)
            self.log(f"📁 Deleted orphaned folder: {folder_id}")
        except Exception as e:
            self.log(f"❌ Failed to delete folder {folder_id}: {str(e)}")

    def _is_safe_path(self, file_path):
        """Check if path is safe (within watched directory)"""
        try:
            resolved = Path(file_path).resolve()
            return resolved.is_relative_to(self.watch_path.resolve())
        except:
            return False

    def _get_relative_path(self, file_path):
        """Get relative path from watch directory"""
        return str(Path(file_path).relative_to(self.watch_path)).replace('\\', '/')


class PrintagoWatchGUI:
    def __init__(self, root):
        self.root = root
        self.root.title("Printago Folder Watch")
        self.root.geometry("800x600")
        self.root.resizable(True, True)

        self.observer = None
        self.client = None
        self.config = self.load_config()

        self.create_widgets()
        self.load_saved_config()

    def create_widgets(self):
        """Create GUI widgets"""
        # Main container
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))

        # Title
        title = ttk.Label(main_frame, text="🗂️ Printago Folder Watch", font=('Arial', 16, 'bold'))
        title.grid(row=0, column=0, columnspan=3, pady=10)

        subtitle = ttk.Label(main_frame, text="Automatically sync your files to Printago")
        subtitle.grid(row=1, column=0, columnspan=3, pady=(0, 20))

        # Configuration section
        config_frame = ttk.LabelFrame(main_frame, text="Configuration", padding="10")
        config_frame.grid(row=2, column=0, columnspan=3, sticky=(tk.W, tk.E), pady=10)

        # Watch directory
        ttk.Label(config_frame, text="Watch Directory:").grid(row=0, column=0, sticky=tk.W, pady=5)
        self.watch_path_var = tk.StringVar()
        ttk.Entry(config_frame, textvariable=self.watch_path_var, width=50, state='readonly').grid(row=0, column=1, pady=5, padx=5)
        ttk.Button(config_frame, text="Browse", command=self.select_directory).grid(row=0, column=2, pady=5)

        # API URL
        ttk.Label(config_frame, text="Printago API URL:").grid(row=1, column=0, sticky=tk.W, pady=5)
        self.api_url_var = tk.StringVar()
        ttk.Entry(config_frame, textvariable=self.api_url_var, width=50).grid(row=1, column=1, pady=5, padx=5, columnspan=2, sticky=(tk.W, tk.E))

        # API Key
        ttk.Label(config_frame, text="API Key:").grid(row=2, column=0, sticky=tk.W, pady=5)
        self.api_key_var = tk.StringVar()
        ttk.Entry(config_frame, textvariable=self.api_key_var, width=50, show="*").grid(row=2, column=1, pady=5, padx=5, columnspan=2, sticky=(tk.W, tk.E))

        # Store ID
        ttk.Label(config_frame, text="Store ID:").grid(row=3, column=0, sticky=tk.W, pady=5)
        self.store_id_var = tk.StringVar()
        ttk.Entry(config_frame, textvariable=self.store_id_var, width=50).grid(row=3, column=1, pady=5, padx=5, columnspan=2, sticky=(tk.W, tk.E))

        # Buttons
        button_frame = ttk.Frame(main_frame)
        button_frame.grid(row=3, column=0, columnspan=3, pady=10)

        self.save_btn = ttk.Button(button_frame, text="Save Configuration", command=self.save_config_clicked)
        self.save_btn.grid(row=0, column=0, padx=5)

        self.start_btn = ttk.Button(button_frame, text="Start Watching", command=self.start_watching, state='disabled')
        self.start_btn.grid(row=0, column=1, padx=5)

        self.stop_btn = ttk.Button(button_frame, text="Stop Watching", command=self.stop_watching, state='disabled')
        self.stop_btn.grid(row=0, column=2, padx=5)

        # Status
        self.status_var = tk.StringVar(value="Ready")
        status_label = ttk.Label(main_frame, textvariable=self.status_var, relief=tk.SUNKEN, anchor=tk.W)
        status_label.grid(row=4, column=0, columnspan=3, sticky=(tk.W, tk.E), pady=5)

        # Activity log
        log_frame = ttk.LabelFrame(main_frame, text="Activity Log", padding="10")
        log_frame.grid(row=5, column=0, columnspan=3, sticky=(tk.W, tk.E, tk.N, tk.S), pady=10)

        self.log_text = scrolledtext.ScrolledText(log_frame, height=15, wrap=tk.WORD)
        self.log_text.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))

        # Configure grid weights
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)
        main_frame.columnconfigure(1, weight=1)
        main_frame.rowconfigure(5, weight=1)
        log_frame.columnconfigure(0, weight=1)
        log_frame.rowconfigure(0, weight=1)

    def select_directory(self):
        """Open directory selection dialog"""
        directory = filedialog.askdirectory(title="Select Directory to Watch")
        if directory:
            self.watch_path_var.set(directory)
            self.log_message(f"Directory selected: {directory}")

    def save_config_clicked(self):
        """Save configuration"""
        watch_path = self.watch_path_var.get().strip()
        api_url = self.api_url_var.get().strip()
        api_key = self.api_key_var.get().strip()
        store_id = self.store_id_var.get().strip()

        if not all([watch_path, api_url, api_key, store_id]):
            messagebox.showerror("Error", "All fields are required")
            return

        if not api_url.startswith(('http://', 'https://')):
            messagebox.showerror("Error", "Invalid API URL format")
            return

        # Save configuration
        self.config = {
            'watch_path': watch_path,
            'api_url': api_url,
            'api_key': api_key,
            'store_id': store_id
        }

        with open(CONFIG_FILE, 'w') as f:
            json.dump(self.config, f)

        # Initialize client with store_id
        self.client = PrintagoClient(api_url, api_key, store_id)

        self.status_var.set("Configuration saved successfully")
        self.start_btn['state'] = 'normal'
        self.log_message("✅ Configuration saved and validated")

    def start_watching(self):
        """Start watching directory"""
        if not self.config or not self.client:
            messagebox.showerror("Error", "Please save configuration first")
            return

        watch_path = self.config['watch_path']

        if not os.path.exists(watch_path):
            messagebox.showerror("Error", f"Directory does not exist: {watch_path}")
            return

        # Create observer
        event_handler = FileWatchHandler(watch_path, self.client, self.log_message)

        # Perform initial sync of existing files
        self.log_message("🚀 Performing initial sync...")
        threading.Thread(target=event_handler.initial_sync, daemon=True).start()

        self.observer = Observer()
        self.observer.schedule(event_handler, watch_path, recursive=True)
        self.observer.start()

        self.status_var.set(f"Watching: {watch_path}")
        self.start_btn['state'] = 'disabled'
        self.stop_btn['state'] = 'normal'
        self.save_btn['state'] = 'disabled'
        self.log_message(f"👀 Started watching: {watch_path}")

    def stop_watching(self):
        """Stop watching directory"""
        if self.observer:
            self.observer.stop()
            self.observer.join()
            self.observer = None

        self.status_var.set("Stopped")
        self.start_btn['state'] = 'normal'
        self.stop_btn['state'] = 'disabled'
        self.save_btn['state'] = 'normal'
        self.log_message("⏸️ Stopped watching")

    def log_message(self, message):
        """Add message to log"""
        timestamp = time.strftime("%H:%M:%S")
        self.log_text.insert(tk.END, f"[{timestamp}] {message}\n")
        self.log_text.see(tk.END)

    def load_config(self):
        """Load saved configuration"""
        try:
            if os.path.exists(CONFIG_FILE):
                with open(CONFIG_FILE, 'r') as f:
                    return json.load(f)
        except:
            pass
        return {}

    def load_saved_config(self):
        """Load configuration into GUI"""
        # Load from .env file first (for testing), then from config.json
        api_url = ENV_VARS.get('PRINTAGO_API_URL', '')
        api_key = ENV_VARS.get('PRINTAGO_API_KEY', '')
        store_id = ENV_VARS.get('PRINTAGO_STORE_ID', '')

        if self.config:
            self.watch_path_var.set(self.config.get('watch_path', ''))
            self.api_url_var.set(self.config.get('api_url', api_url))
            self.api_key_var.set(self.config.get('api_key', api_key))
            self.store_id_var.set(self.config.get('store_id', store_id))

            if all([self.config.get('watch_path'), self.config.get('api_url'), self.config.get('api_key'), self.config.get('store_id')]):
                self.client = PrintagoClient(self.config['api_url'], self.config['api_key'], self.config['store_id'])
                self.start_btn['state'] = 'normal'
        else:
            # Pre-fill from .env if available
            if api_url:
                self.api_url_var.set(api_url)
            if api_key:
                self.api_key_var.set(api_key)
            if store_id:
                self.store_id_var.set(store_id)

    def on_closing(self):
        """Handle window close"""
        if self.observer:
            self.stop_watching()
        self.root.destroy()


def main():
    root = tk.Tk()
    app = PrintagoWatchGUI(root)
    root.protocol("WM_DELETE_WINDOW", app.on_closing)
    root.mainloop()


if __name__ == "__main__":
    main()
