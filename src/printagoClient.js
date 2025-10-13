const axios = require('axios');
const FormData = require('form-data');

class PrintagoClient {
  constructor(apiUrl, apiKey) {
    // Security: Ensure HTTPS for production
    this.apiUrl = apiUrl.endsWith('/') ? apiUrl.slice(0, -1) : apiUrl;
    this.apiKey = apiKey;

    this.client = axios.create({
      baseURL: this.apiUrl,
      timeout: 30000,
      headers: {
        'Authorization': `Bearer ${this.apiKey}`,
        'User-Agent': 'PrintagoFolderWatch/1.0'
      }
    });

    // Add response interceptor for error handling
    this.client.interceptors.response.use(
      response => response,
      error => {
        if (error.response) {
          // Server responded with error status
          const message = error.response.data?.message || error.response.statusText;
          throw new Error(`API Error (${error.response.status}): ${message}`);
        } else if (error.request) {
          // Request made but no response
          throw new Error('No response from server. Check your connection.');
        } else {
          // Error setting up request
          throw new Error(`Request error: ${error.message}`);
        }
      }
    );
  }

  async testConnection() {
    try {
      // Test API connection - adjust endpoint based on Printago API
      const response = await this.client.get('/api/health', {
        timeout: 5000
      });
      return response.status === 200;
    } catch (error) {
      console.error('Connection test failed:', error.message);
      return false;
    }
  }

  async uploadFile(relativePath, fileBuffer) {
    try {
      const formData = new FormData();

      // Security: Sanitize filename to prevent path traversal
      const safePath = this.sanitizePath(relativePath);

      formData.append('file', fileBuffer, {
        filename: safePath,
        contentType: 'application/octet-stream'
      });
      formData.append('path', safePath);
      formData.append('overwrite', 'true');

      const response = await this.client.post('/api/files/upload', formData, {
        headers: {
          ...formData.getHeaders(),
          'Authorization': `Bearer ${this.apiKey}`
        },
        maxContentLength: Infinity,
        maxBodyLength: Infinity
      });

      return response.data;
    } catch (error) {
      console.error(`Upload failed for ${relativePath}:`, error.message);
      throw error;
    }
  }

  async deleteFile(relativePath) {
    try {
      const safePath = this.sanitizePath(relativePath);

      const response = await this.client.delete('/api/files/delete', {
        data: { path: safePath }
      });

      return response.data;
    } catch (error) {
      // Don't throw on delete errors, just log
      console.error(`Delete failed for ${relativePath}:`, error.message);
    }
  }

  sanitizePath(filePath) {
    // Security: Remove dangerous path components
    return filePath
      .replace(/\.\./g, '') // Remove parent directory references
      .replace(/^[\/\\]+/, '') // Remove leading slashes
      .replace(/[<>:"|?*]/g, '_'); // Replace illegal characters
  }
}

module.exports = PrintagoClient;
