import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { PlanRequestRevision, runPlanRequest } from '../planRequestLifecycle';

export function useGuardedRequest() {
    const revision = useRef(new PlanRequestRevision());
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    useEffect(() => () => revision.current.invalidate(), []);

    const invalidate = useCallback(() => {
        revision.current.invalidate();
        setLoading(false);
        setError('');
    }, []);

    const run = useCallback(async <T,>(request: () => Promise<T>, onSuccess: (value: T) => void, onError?: (reason: unknown) => void) => {
        const current = revision.current.begin();
        setLoading(true);
        setError('');
        await runPlanRequest(revision.current, current, request, {
            onSuccess,
            onError: reason => {
                setError(reason instanceof Error ? reason.message : 'The request could not be completed.');
                onError?.(reason);
            },
            onFinally: () => setLoading(false),
        });
    }, []);

    return useMemo(() => ({ loading, error, setError, invalidate, run }), [loading, error, invalidate, run]);
}
