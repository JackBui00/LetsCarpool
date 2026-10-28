export type CapabilityRole = 'invite' | 'organizer' | 'participant';

export type CapabilityStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

export function readCapabilityFragment(
    pathname: string,
    search: string,
    hash: string,
    storage: CapabilityStorage,
    replaceUrl: (url: string) => void,
) {
    const match = pathname.match(/^\/(?:join|manage|trip)\/([^/]+)\/?$/);
    if (!match || !hash) return null;
    const tripId = decodeURIComponent(match[1]);
    const fragment = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
    const invite = fragment.get('invite');
    const owner = fragment.get('owner');
    const role: CapabilityRole | null = invite ? 'invite' : owner ? 'organizer' : null;
    const token = invite ?? owner;
    if (!role || !token) return null;
    try {
        storage.setItem(capabilityStorageKey(role, tripId), token);
        replaceUrl(`${pathname}${search}`);
        return { role, tripId };
    } catch {
        replaceUrl(`${pathname}${search}`);
        return { role, tripId, storageError: true };
    }
}

export function capabilityStorageKey(role: CapabilityRole, tripId: string) {
    return `gather.capability.${role}.${tripId}`;
}

export function getCapability(role: CapabilityRole, tripId: string, storage?: CapabilityStorage) {
    try { return (storage ?? sessionStorage).getItem(capabilityStorageKey(role, tripId)); } catch { return null; }
}

export function setCapability(role: CapabilityRole, tripId: string, token: string, storage?: CapabilityStorage) {
    (storage ?? sessionStorage).setItem(capabilityStorageKey(role, tripId), token);
}

export function clearCapability(role: CapabilityRole, tripId: string, storage?: CapabilityStorage) {
    try { (storage ?? sessionStorage).removeItem(capabilityStorageKey(role, tripId)); } catch { /* Storage may be unavailable. */ }
}
