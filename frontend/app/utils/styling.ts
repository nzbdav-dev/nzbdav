export function className(classNames: (string | false | null | undefined)[]) {
    return {
        className: classNames.filter(Boolean).join(' ')
    }
}

export function classNames(classNames: (string | false | null | undefined)[]) {
    return classNames.filter(Boolean).join(' ')
}