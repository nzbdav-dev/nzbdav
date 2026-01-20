import { useCallback } from "react";
import { useDropzone } from "react-dropzone";
import type { UploadingFile } from "../route";

export function useQueueDropzone(
    setUploadingFiles: (value: React.SetStateAction<UploadingFile[]>) => void,
    uploadQueueRef: React.RefObject<UploadingFile[]>,
    manualCategoryRef: React.RefObject<string>,
) {

    const onDrop = useCallback((acceptedFiles: File[]) => {
        const newFiles: UploadingFile[] = acceptedFiles.map(file => ({
            file,
            queueSlot: {
                isUploading: true,
                nzo_id: `upload-${Date.now()}-${Math.random()}`,
                priority: 'Normal',
                filename: file.name,
                cat: manualCategoryRef.current,
                percentage: "0",
                true_percentage: "0",
                status: "pending",
                mb: (file.size / (1024 * 1024)).toFixed(2),
                mbleft: (file.size / (1024 * 1024)).toFixed(2),
            }
        }));

        setUploadingFiles(files => [...files, ...newFiles]);
        uploadQueueRef.current = [...uploadQueueRef.current, ...newFiles];
    }, []);

    return useDropzone({
        accept: { 'application/x-nzb': ['.nzb'] },
        onDrop,
        noClick: true,
        noKeyboard: true,
    });
}