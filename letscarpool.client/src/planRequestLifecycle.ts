export type RequestResultHandlers<T> = {
    onSuccess: (value: T) => void;
    onError: (error: unknown) => void;
    onFinally: () => void;
};

export class PlanRequestRevision {
    private revision = 0;

    begin() {
        this.revision += 1;
        return this.revision;
    }

    invalidate() {
        this.revision += 1;
    }

    isCurrent(requestRevision: number) {
        return requestRevision === this.revision;
    }
}

export async function runPlanRequest<T>(
    revisions: PlanRequestRevision,
    requestRevision: number,
    request: () => Promise<T>,
    handlers: RequestResultHandlers<T>,
): Promise<'success' | 'error' | 'stale'> {
    try {
        const value = await request();
        if (!revisions.isCurrent(requestRevision)) return 'stale';
        handlers.onSuccess(value);
        return 'success';
    } catch (error) {
        if (!revisions.isCurrent(requestRevision)) return 'stale';
        handlers.onError(error);
        return 'error';
    } finally {
        if (revisions.isCurrent(requestRevision)) handlers.onFinally();
    }
}
