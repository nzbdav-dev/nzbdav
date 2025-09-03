import pageStyles from "../../route.module.css"
import { useCallback, useRef } from "react";
import styles from "./empty-queue.module.css"
import { useDropzone, type FileWithPath } from 'react-dropzone'
import { className } from "~/utils/styling";
import { useFetcher } from "react-router";

export function EmptyQueue() {
    const fetcher = useFetcher();
    const formRef = useRef<HTMLFormElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);
    const isSubmitting = (fetcher.state === 'submitting');

    const { getRootProps, getInputProps, isDragActive } = useDropzone({
        accept: { 'application/x-nzb': ['.nzb'] },
        onDrop: useCallback((acceptedFiles: FileWithPath[]) => {
            const dataTransfer = new DataTransfer();
            acceptedFiles.forEach((file) => {
                const newFile = new File([file], file.name, {
                    type: 'application/x-nzb',
                    lastModified: file.lastModified,
                });
                dataTransfer.items.add(newFile);
            });
            if (inputRef?.current) {
                inputRef.current.files = dataTransfer.files;
                fetcher.submit(formRef.current);
            }
        }, [])
    });

    return (
        <fetcher.Form ref={formRef} method="POST" encType="multipart/form-data">
            <div className={pageStyles["section-title"]}>
                <h3>Queue</h3>
            </div>
            <div {...className([styles.container, isDragActive && styles["drag-active"]])}  {...getRootProps()}>
                <input {...getInputProps()} />
                <input ref={inputRef} name="nzbFile" type="file" style={{ display: 'none' }} />

                {isSubmitting && <>
                    <div>Uploading...</div>
                </>}

                {/* default view */}
                {!isSubmitting && !isDragActive && <>
                    <div className={styles["upload-icon"]}></div>
                    <br />
                    <div>Queue is empty.</div>
                    <div>Upload an *.nzb file</div>
                </>}

                {/* when dragging a file */}
                {!isSubmitting && isDragActive && <>
                    <div className={styles["drop-icon"]}></div>
                    <br />
                    <div>Drop your *.nzb file</div>
                </>}
            </div>
        </fetcher.Form>
    );
}