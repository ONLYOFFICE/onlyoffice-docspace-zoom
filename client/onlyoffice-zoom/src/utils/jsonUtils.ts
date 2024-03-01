function toLowerCaseProps<T>(obj: any): T {
    return Object.entries(obj).reduce((a, [key, val]) => {
        a[key.charAt(0).toLowerCase() + key.slice(1)] = val;
        return a;
    }, {} as any) as T;
}

export default toLowerCaseProps;