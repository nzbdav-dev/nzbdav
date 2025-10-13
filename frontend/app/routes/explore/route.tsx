import type { Route } from "./+types/route";
import { Breadcrumbs } from "./breadcrumbs/breadcrumbs";
import styles from "./route.module.css"
import { Link, redirect, useLocation, useNavigation } from "react-router";
import { backendClient, type DirectoryItem } from "~/clients/backend-client.server";
import { useCallback } from "react";
import { lookup as getMimeType } from 'mime-types';
import { getDownloadKey } from "~/auth/downloads.server";
import { Loading } from "../_index/components/loading/loading";
import { formatFileSize } from "~/utils/file-size";

export type ExplorePageData = {
    parentDirectories: string[],
    items: (DirectoryItem | ExploreFile)[],
}

export type ExploreFile = DirectoryItem & {
    mimeType: string,
    downloadKey: string,
}


export async function loader({ request }: Route.LoaderArgs) {
    // if path ends in trailing slash, remove it
    if (request.url.endsWith('/')) return redirect(request.url.slice(0, -1));

    // load items from backend
    let path = getWebdavPath(new URL(request.url).pathname);
    return {
        parentDirectories: getParentDirectories(path),
        items: (await backendClient.listWebdavDirectory(path)).map(x => {
            if (x.isDirectory) return x;
            return {
                ...x,
                mimeType: getMimeType(x.name),
                downloadKey: getDownloadKey(`${path}/${x.name}`)
            };
        })
    }
}

export default function Explore({ loaderData }: Route.ComponentProps) {
    return (
        <Body {...loaderData} />
    );
}

function Body(props: ExplorePageData) {
    const location = useLocation();
    const navigation = useNavigation();
    const isNavigating = Boolean(navigation.location);

    const items = props.items;
    const parentDirectories = isNavigating
        ? getParentDirectories(getWebdavPath(navigation.location!.pathname))
        : props.parentDirectories;

    const getDirectoryPath = useCallback((directoryName: string) => {
        return `${location.pathname}/${directoryName}`
    }, [location.pathname]);

    const getFilePath = useCallback((file: ExploreFile) => {
        var pathname = getWebdavPath(location.pathname);
        return `/view/${pathname}/${file.name}?downloadKey=${file.downloadKey}`;
    }, [location.pathname]);

    return (
        <div className={styles.container}>
            <Breadcrumbs parentDirectories={parentDirectories} />
            {!isNavigating &&
                <div>
                    {items.filter(x => x.isDirectory).map((x, index) =>
                        <Link key={`${index}_dir_item`} to={getDirectoryPath(x.name)} className={getClassName(x)}>
                            <div className={styles["directory-icon"]} />
                            <div className={styles["item-name"]}>{x.name}</div>
                        </Link>
                    )}
                    {items.filter(x => !x.isDirectory).map((x, index) =>
                        <a key={`${index}_file_item`} href={getFilePath(x as ExploreFile)} className={getClassName(x)}>
                            <div className={getIcon(x as ExploreFile)} />
                            <div className={styles["item-info"]}>
                                <div className={styles["item-name"]}>{x.name}</div>
                                <div className={styles["item-size"]}>{formatFileSize(x.size)}</div>
                            </div>
                        </a>
                    )}
                </div>
            }
            {isNavigating && <Loading className={styles.loading} />}
        </div >
    );
}

function getIcon(file: ExploreFile) {
    if (file.name.toLowerCase().endsWith(".mkv")) return styles["video-icon"];
    if (file.mimeType && file.mimeType.startsWith("video")) return styles["video-icon"];
    if (file.mimeType && file.mimeType.startsWith("image")) return styles["image-icon"];
    return styles["file-icon"];
}

function getWebdavPath(pathname: string): string {
    pathname = decodeURIComponent(pathname);
    if (pathname.startsWith("/")) pathname = pathname.slice(1);
    if (pathname.startsWith("explore")) pathname = pathname.slice(7);
    if (pathname.startsWith("/")) pathname = pathname.slice(1);
    return pathname;
}

function getParentDirectories(webdavPath: string): string[] {
    return webdavPath == "" ? [] : webdavPath.split('/');
}

function getClassName(item: DirectoryItem | ExploreFile) {
    let className = styles.item;
    if (item.name.startsWith('.')) className += " " + styles.hidden;
    return className;
}