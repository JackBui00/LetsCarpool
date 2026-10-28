export type PlaceSource = 'demo' | 'google' | 'unresolved';

export type PlaceSelection = {
    label: string;
    latitude: number | null;
    longitude: number | null;
    placeId: string | null;
    source: PlaceSource;
};

export function demoPlace(label: string, latitude: number, longitude: number): PlaceSelection {
    return { label, latitude, longitude, placeId: null, source: 'demo' };
}

export function unresolvedPlace(label: string): PlaceSelection {
    return { label, latitude: null, longitude: null, placeId: null, source: 'unresolved' };
}

export function editPlaceLabel(label: string): PlaceSelection {
    return unresolvedPlace(label);
}

export function isPlaceResolved(place: PlaceSelection) {
    return place.source !== 'unresolved'
        && place.latitude !== null
        && place.longitude !== null
        && Number.isFinite(place.latitude)
        && Number.isFinite(place.longitude);
}

export function hasGoogleMapsKey(key: string | undefined) {
    return Boolean(key?.trim());
}

export function enabledGoogleMapsKey(enabled: string | undefined, key: string | undefined) {
    return enabled === 'true' && hasGoogleMapsKey(key) ? key : undefined;
}

export function isPlanReady(key: string | undefined, places: PlaceSelection[]) {
    return hasGoogleMapsKey(key) && places.length > 0 && places.every(isPlaceResolved);
}

export type Coordinate = { lat: number; lng: number };
export type GooglePlace = { formattedAddress?: string; displayName?: string; location?: { lat: () => number; lng: () => number }; id?: string; fetchFields: (options: { fields: string[] }) => Promise<void> };
export type GoogleMapsApi = {
    importLibrary: (name: string) => Promise<unknown>;
    LatLngBounds: new () => { extend: (location: Coordinate) => void; getCenter: () => Coordinate };
};
let mapsPromise: Promise<GoogleMapsApi> | undefined;

export function loadGoogleMaps(key: string | undefined): Promise<GoogleMapsApi> {
    if (!hasGoogleMapsKey(key)) return Promise.reject(new Error('Google Maps location search is not configured.'));
    if (mapsPromise) return mapsPromise;
    mapsPromise = new Promise<GoogleMapsApi>((resolve, reject) => {
        const existing = document.querySelector<HTMLScriptElement>('script[data-gather-google-maps]');
        if (existing) {
            existing.addEventListener('load', () => resolve((window as unknown as { google: { maps: GoogleMapsApi } }).google.maps), { once: true });
            existing.addEventListener('error', () => reject(new Error('Google Maps could not be loaded.')), { once: true });
            return;
        }
        const script = document.createElement('script');
        script.dataset.gatherGoogleMaps = 'true';
        script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key!)}&v=weekly&loading=async`;
        script.async = true;
        script.onload = () => resolve((window as unknown as { google: { maps: GoogleMapsApi } }).google.maps);
        script.onerror = () => reject(new Error('Google Maps could not be loaded. Check the key restrictions and enabled Maps JavaScript API.'));
        document.head.append(script);
    }).catch(error => {
        mapsPromise = undefined;
        throw error;
    });
    return mapsPromise!;
}
