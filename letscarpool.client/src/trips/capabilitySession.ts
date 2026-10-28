export type CapabilityRole = 'invite' | 'organizer' | 'participant';

export type CapabilityStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

export function readCapabilityFragment(
    pathname: string,
    search: string,
    hash: string,
    storage: CapabilityStorage,
    replaceUrl: (url: string) => void,
) {
    const match = pathname.match(/^\/(join|trips|manage|trip)\/([^/]+)\/?$/);
    if (!match || !hash) return null;
    const route = match[1];
    const tripId = decodeURIComponent(match[2]);
    const fragment = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
    const role: CapabilityRole | null = route === 'join' ? 'invite' : route === 'trips' || route === 'manage' ? 'organizer' : null;
    const token = role === 'invite' ? fragment.get('invite') : role === 'organizer' ? fragment.get('organizer') ?? fragment.get('owner') : null;
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
