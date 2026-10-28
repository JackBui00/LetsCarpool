import type { LocationDto } from './tripTypes';

export type JoinRequestBody = {
    name: string;
    origin: LocationDto;
    canDrive: boolean;
    passengerSeats: number;
    canMeet: boolean;
    maxDetourMinutes: number;
    locationDisclosureAccepted: true;
};

export type JoinAttemptRecord = {
    idempotencyKey: string;
    body: JoinRequestBody;
    status: 'pending' | 'retryable' | 'uncertain';
    reason?: 'replay' | 'interrupted';
};

export type JoinFailureDisposition = { status: JoinAttemptRecord['status'] | 'rejected'; mayHaveSucceeded: boolean };

export async function joinAttemptStorageKey(tripId: string, inviteCapability: string, cryptoApi: Crypto = crypto): Promise<string> {
    const digest = await cryptoApi.subtle.digest('SHA-256', new TextEncoder().encode(inviteCapability));
    const hex = Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
    return `gather.join-attempt.${encodeURIComponent(tripId)}.${hex}`;
}

export function readJoinAttempt(storage: Pick<Storage, 'getItem'>, key: string): JoinAttemptRecord | null {
    try {
        const value = storage.getItem(key);
        if (!value) return null;
        const parsed: unknown = JSON.parse(value);
        if (!isRecord(parsed) || typeof parsed.idempotencyKey !== 'string' || !isUuid(parsed.idempotencyKey)
            || !isRequestBody(parsed.body) || !['pending', 'retryable', 'uncertain'].includes(String(parsed.status))
            || (parsed.reason !== undefined && parsed.reason !== 'replay' && parsed.reason !== 'interrupted')) return null;
        return parsed as JoinAttemptRecord;
    } catch {
        return null;
    }
}

export function writeJoinAttempt(storage: Pick<Storage, 'setItem'>, key: string, record: JoinAttemptRecord): boolean {
    try { storage.setItem(key, JSON.stringify(record)); return true; }
    catch { return false; }
}

export function removeJoinAttempt(storage: Pick<Storage, 'removeItem'>, key: string): void {
    try { storage.removeItem(key); } catch { /* Session storage may be unavailable. */ }
}

export function tripMayHaveJoinedKey(tripId: string) {
    return `gather.join-may-exist.${encodeURIComponent(tripId)}`;
}

export function markTripMayHaveJoined(storage: Pick<Storage, 'setItem'>, tripId: string): boolean {
    try { storage.setItem(tripMayHaveJoinedKey(tripId), 'true'); return true; }
    catch { return false; }
}

export function hasTripMayHaveJoined(storage: Pick<Storage, 'getItem'>, tripId: string): boolean {
    try { return storage.getItem(tripMayHaveJoinedKey(tripId)) === 'true'; }
    catch { return false; }
}

export function clearTripMayHaveJoined(storage: Pick<Storage, 'removeItem'>, tripId: string): void {
    try { storage.removeItem(tripMayHaveJoinedKey(tripId)); } catch { /* Session storage may be unavailable. */ }
}

export function legacyJoinAttemptKey(tripId: string) {
    return `gather.join-attempt.${tripId}`;
}

export function classifyJoinFailure(status: number, problemCode?: string): JoinFailureDisposition {
    if (problemCode === 'join_already_processed') return { status: 'uncertain', mayHaveSucceeded: true };
    if (status === 0) return { status: 'retryable', mayHaveSucceeded: true };
    if (status === 429) return { status: 'retryable', mayHaveSucceeded: false };
    if (status === 400 || status === 404 || status === 422 || problemCode === 'trip_capacity_reached' || problemCode === 'revision_conflict') return { status: 'rejected', mayHaveSucceeded: false };
    return { status: 'uncertain', mayHaveSucceeded: true };
}

function isRequestBody(value: unknown): value is JoinRequestBody {
    if (!isRecord(value) || typeof value.name !== 'string' || typeof value.canDrive !== 'boolean' || typeof value.canMeet !== 'boolean'
        || typeof value.passengerSeats !== 'number' || !Number.isInteger(value.passengerSeats) || value.passengerSeats < 0 || value.passengerSeats > 8
        || typeof value.maxDetourMinutes !== 'number' || !Number.isFinite(value.maxDetourMinutes) || value.maxDetourMinutes < 0 || value.maxDetourMinutes > 1440
        || value.locationDisclosureAccepted !== true || !isRecord(value.origin)) return false;
    return typeof value.origin.label === 'string' && Number.isFinite(value.origin.latitude) && Number.isFinite(value.origin.longitude);
}

function isUuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
