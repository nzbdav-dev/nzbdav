import { useEffect } from "react";
import type { UploadingFile } from "../route";

export function initializeUploadController(
    isUploadingRef: React.RefObject<boolean>,
    uploadQueueRef: React.RefObject<UploadingFile[]>,
    uploadingFiles: UploadingFile[],
    setUploadingFiles: (value: React.SetStateAction<UploadingFile[]>) => void,
) {
    useEffect(() => {
        processUploadQueue(isUploadingRef, uploadQueueRef, setUploadingFiles);
    }, [uploadingFiles]);
}

async function processUploadQueue(
    isUploadingRef: React.RefObject<boolean>,
    uploadQueueRef: React.RefObject<UploadingFile[]>,
    setUploadingFiles: (value: React.SetStateAction<UploadingFile[]>) => void
) {
    if (isUploadingRef.current || uploadQueueRef.current.length === 0) return;

    isUploadingRef.current = true;
    const fileToUpload = uploadQueueRef.current[0];

    setUploadingFiles(files => files.map(f =>
        f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id
            ? { ...f, queueSlot: { ...f.queueSlot, status: 'uploading' } }
            : f
    ));

    try {
        const xhr = new XMLHttpRequest();
        const formData = new FormData();
        formData.append('nzbFile', fileToUpload.file, fileToUpload.file.name);

        xhr.responseType = 'json';
        xhr.upload.addEventListener('progress', (e) => {
            if (e.lengthComputable) {
                const progress = Math.round((e.loaded / e.total) * 100);
                setUploadingFiles(files => files.map(f =>
                    f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id
                        ? {
                            ...f,
                            queueSlot: {
                                ...f.queueSlot,
                                percentage: progress.toString(),
                                true_percentage: progress.toString()
                            }
                        }
                        : f
                ));
            }
        });

        var response: any = await new Promise<void>((resolve, reject) => {
            xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    resolve(xhr.response);
                } else {
                    const errorMessage = xhr.response.error || `Upload failed with status ${xhr.status}`;
                    reject(new Error(errorMessage));
                }
            });
            xhr.addEventListener('error', () => reject(new Error('Upload failed')));
            xhr.addEventListener('abort', () => reject(new Error('Upload aborted')));

            xhr.open('POST', `/api?mode=addfile&cat=${fileToUpload.queueSlot.cat}&priority=0&pp=0`);
            xhr.send(formData);
        });

        if (response.status == false) {
            throw new Error(response.error);
        }

    } catch (error) {
        setUploadingFiles(files => files.map(f =>
            f.queueSlot.nzo_id === fileToUpload.queueSlot.nzo_id ? {
                ...f,
                queueSlot: {
                    ...f.queueSlot,
                    status: 'upload failed',
                    error: error instanceof Error ? error.message : 'Upload failed'
                }
            } : f
        ));
    }

    uploadQueueRef.current = uploadQueueRef.current.filter(x => x !== fileToUpload);
    isUploadingRef.current = false;

    if (uploadQueueRef.current.length > 0) {
        processUploadQueue(isUploadingRef, uploadQueueRef, setUploadingFiles);
    }
}