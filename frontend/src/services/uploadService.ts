import axios from 'axios';

export interface BlobReference {
  blobId: string;
  storageAccount: string;
  container: string;
  blobName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
  expiresAt: string;
  accessUrl: string;
}

export interface UploadResponse {
  success: boolean;
  blobReference?: BlobReference;
  error?: string;
}

export const uploadService = {
  async uploadFile(file: File): Promise<UploadResponse> {
    try {
      console.log('📤 Uploading file to blob storage:', file.name);
      
      const formData = new FormData();
      formData.append('file', file);

      const response = await axios.post('/api/uploads', formData, {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      });

      const blobReference: BlobReference = response.data;
      console.log('✅ File uploaded successfully:', blobReference);

      return {
        success: true,
        blobReference
      };
    } catch (error: any) {
      console.error('❌ File upload failed:', error);
      const errorMessage = error.response?.data?.errors?.[0]?.message || 
                          error.response?.data?.message || 
                          error.message || 
                          'Unknown upload error';
      return {
        success: false,
        error: errorMessage
      };
    }
  }
};