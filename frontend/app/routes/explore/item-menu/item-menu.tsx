import { useCallback, useState, type ReactNode } from "react"
import type { ExploreFile } from "../route"
import { DropdownOptions } from "~/routes/explore/dropdown-options/dropdown-options"
import { classNames } from "~/utils/styling"

export type ItemMenuProps = {
    className?: string
    openClassName?: string
    exploreFile: ExploreFile,
    previewPath: string,
}

export function ItemMenu({ className, openClassName, exploreFile, previewPath }: ItemMenuProps): ReactNode {
    const [isOpen, setIsOpen] = useState(false);
    const exportNzbUrl = `/api/download-nzb?nzbBlobId=${exploreFile.nzbBlobId}`;
    const downloadUrl = `${previewPath}&download=true`;

    const onClick = useCallback((e: React.MouseEvent) => {
        e.stopPropagation();
        setIsOpen(x => !x);
    }, []);

    return (
        <>
            <div className={classNames([className, isOpen && openClassName])} onClick={onClick}>
                ⋯
            </div>
            <DropdownOptions isOpen={isOpen} onClose={() => setIsOpen(false)} options={[
                { option: <Preview />, linkTo: previewPath },
                { option: <Download />, linkTo: downloadUrl },
                !!exploreFile.nzbBlobId ? { option: <ExportNzb />, linkTo: exportNzbUrl } : undefined
            ]} />
        </>
    );
}

export function Preview(): ReactNode {
    return (
        <><EyeIcon /> Preview</>
    );
}

export function Download(): ReactNode {
    return (
        <><DownloadIcon /> Download</>
    );
}

export function ExportNzb(): ReactNode {
    return (
        <><ExportIcon /> Export NZB</>
    );
}

export function Remove(): ReactNode {
    return (
        <><TrashIcon /> Remove</>
    );
}

function EyeIcon() {
    return (
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" style={{ marginRight: "8px", verticalAlign: "-2px" }}>
            <path d="M16 8s-3-5.5-8-5.5S0 8 0 8s3 5.5 8 5.5S16 8 16 8zM1.173 8a13.133 13.133 0 0 1 1.66-2.043C4.12 4.668 5.88 3.5 8 3.5c2.12 0 3.879 1.168 5.168 2.457A13.133 13.133 0 0 1 14.828 8c-.058.087-.122.183-.195.288-.335.48-.83 1.12-1.465 1.755C11.879 11.332 10.119 12.5 8 12.5c-2.12 0-3.879-1.168-5.168-2.457A13.134 13.134 0 0 1 1.172 8z" />
            <path d="M8 5.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5zM4.5 8a3.5 3.5 0 1 1 7 0 3.5 3.5 0 0 1-7 0z" />
        </svg>
    );
}

function DownloadIcon() {
    return (
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" style={{ marginRight: "8px", verticalAlign: "-2px" }}>
            <path d="M.5 9.9a.5.5 0 0 1 .5.5v2.5a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-2.5a.5.5 0 0 1 1 0v2.5a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2v-2.5a.5.5 0 0 1 .5-.5z" />
            <path d="M7.646 11.854a.5.5 0 0 0 .708 0l3-3a.5.5 0 0 0-.708-.708L8.5 10.293V1.5a.5.5 0 0 0-1 0v8.793L5.354 8.146a.5.5 0 1 0-.708.708l3 3z" />
        </svg>
    );
}

function ExportIcon() {
    return (
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" style={{ marginRight: "8px", verticalAlign: "-2px" }}>
            <path d="M3.5 6a.5.5 0 0 0-.5.5v8a.5.5 0 0 0 .5.5h9a.5.5 0 0 0 .5-.5v-8a.5.5 0 0 0-.5-.5h-2a.5.5 0 0 1 0-1h2A1.5 1.5 0 0 1 14 6.5v8a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 14.5v-8A1.5 1.5 0 0 1 3.5 5h2a.5.5 0 0 1 0 1h-2z" />
            <path d="M7.646.146a.5.5 0 0 1 .708 0l3 3a.5.5 0 0 1-.708.708L8.5 1.707V10.5a.5.5 0 0 1-1 0V1.707L5.354 3.854a.5.5 0 1 1-.708-.708l3-3z" />
        </svg>
    );
}

function TrashIcon() {
    return (
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor" style={{ marginRight: "8px", verticalAlign: "-2px" }}>
            <path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm2.5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm3 .5a.5.5 0 0 0-1 0v6a.5.5 0 0 0 1 0V6z" />
            <path d="M14.5 3a1 1 0 0 1-1 1H13v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4h-.5a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1H6a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1h3.5a1 1 0 0 1 1 1v1zM4.118 4 4 4.059V13a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V4.059L11.882 4H4.118zM2.5 3V2h11v1h-11z" />
        </svg>
    );
}