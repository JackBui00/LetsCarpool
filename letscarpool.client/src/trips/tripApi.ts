export class TripApiError extends Error {
    readonly status: number;
    readonly errors?: Record<string, string[]>;
    readonly retryAfter?: string;
    readonly problemCode?: string;

    constructor(status: number, message: string, errors?: Record<string, string[]>, retryAfter?: string, problemCode?: string) {
        super(message);
        this.status = status;
        this.errors = errors;
        this.retryAfter = retryAfter;
        this.problemCode = problemCode;
        this.name = 'TripApiError';
    }
}

export async function tripApi<T>(path: string, token: string | null, init: RequestInit = {}, fetcher: typeof fetch = fetch): Promise<T> {
    const headers = new Headers(init.headers);
    headers.set('Accept', 'application/json');
    if (init.body) headers.set('Content-Type', 'application/json');
    if (token) headers.set('Authorization', `Bearer ${token}`);
    let response: Response;
    try {
        response = await fetcher(`/api/v1/trips${path}`, { ...init, headers });
    } catch {
        throw new TripApiError(0, 'The trip service could not be reached. Your current input is still on this page.');
    }
    const body = await response.json().catch(() => null) as (T & { type?: string; code?: string; errorCode?: string; error?: string; title?: string; detail?: string; errors?: Record<string, string[]> }) | null;
    if (!response.ok) {
        const fieldErrors = body?.errors;
        const fieldSummary = fieldErrors ? Object.entries(fieldErrors).flatMap(([field, messages]) => messages.map(message => `${field}: ${message}`)).join(' ') : '';
        const problemCode = body?.code ?? body?.errorCode ?? body?.error ?? lastTypeSegment(body?.type) ?? normalizeProblemCode(body?.title);
        const message = response.status === 404
            ? 'This trip link is invalid, expired, or has been revoked.'
            : problemCode === 'join_already_processed'
                ? 'This join request was already processed. If its response was lost, the participant capability cannot be recovered from the invite.'
                : problemCode === 'trip_capacity_reached'
                    ? 'This trip has reached its six participant limit. Ask the organizer before trying again.'
                    : problemCode === 'revision_conflict'
                        ? 'The trip changed while you were editing. Reload the current revision before saving again.'
            : response.status === 409
                ? fieldSummary || body?.detail || body?.title || 'The trip changed or this action was already submitted. Refresh the trip before trying again.'
                : fieldSummary || body?.detail || body?.title || `Trip request failed (${response.status}).`;
        throw new TripApiError(response.status, message, fieldErrors, response.headers.get('Retry-After') ?? undefined, problemCode);
    }
    if (!body && (response.status === 204 || response.status === 205)) return undefined as T;
    if (!body) throw new TripApiError(response.status, 'The trip service returned an empty response.');
    return body as T;
}

function normalizeProblemCode(title?: string) {
    return title?.trim().toLowerCase().replace(/\s+/g, '_');
}

function lastTypeSegment(type?: string) { return type?.split(':').pop()?.split('/').pop()?.split('#').pop(); }
